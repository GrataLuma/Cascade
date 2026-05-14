using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HashFirstAttacker.Attackers;
using HashFirstAttacker.Reports;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// B.5-extended orchestrator: runs all four attacker variants against the same
    /// N-transcript corpus in parallel, with progress persistence, resume support,
    /// Shannon entropy check, and Clopper-Pearson CI.
    /// </summary>
    public static class BigEval
    {
        public sealed class Options
        {
            public int Runs = 300;
            public int B1BudgetSeconds = 1800; // 30 min per transcript
            public long B2B3Pool = 10_000_000;
            public long B7Pool = 1_000_000;  // separate cap; default 1M to bound dedup memory
            public int B4DiscSamples = 1_000_000;
            public string ReportPath = "reports/big_eval_report.md";
            public string CsvPath = "reports/big_eval_results.csv";
            public string ProgressPath = "reports/big_eval_progress.csv";
            public string TranscriptsBin = "reports/transcripts_{N}.bin";
            public string TranscriptsIdx = "reports/transcripts_{N}.idx";
            public bool Resume = false;
            public bool Overwrite = false;
            public bool SkipGen = false;
            public int? Workers = null;
            public int BaseSeed = 0x5EED;
        }

        public static int Run(Options opts)
        {
            string binPath = opts.TranscriptsBin.Replace("{N}", opts.Runs.ToString());
            string idxPath = opts.TranscriptsIdx.Replace("{N}", opts.Runs.ToString());

            // ---- Progress file policy ----
            var alreadyDone = new HashSet<(string attacker, int idx)>();
            if (File.Exists(opts.ProgressPath))
            {
                if (opts.Overwrite)
                {
                    File.Delete(opts.ProgressPath);
                }
                else if (opts.Resume)
                {
                    foreach (var line in File.ReadAllLines(opts.ProgressPath).Skip(1))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int idx)) alreadyDone.Add((parts[1], idx));
                    }
                    Console.Error.WriteLine($"Resume: {alreadyDone.Count} (attacker, transcript) pairs already done.");
                }
                else
                {
                    Console.Error.WriteLine($"ERROR: {opts.ProgressPath} has data. Use --resume to continue or --overwrite to restart.");
                    return 2;
                }
            }
            if (!File.Exists(opts.ProgressPath))
            {
                EnsureDir(opts.ProgressPath);
                File.WriteAllText(opts.ProgressPath, "timestamp_utc,attacker,transcript_index,success,slots_matched,slots_possible,candidates_tried,elapsed_seconds,notes\n");
            }

            // ---- Phase 0: generate or load transcripts ----
            if (opts.SkipGen && !File.Exists(binPath))
            {
                Console.Error.WriteLine($"ERROR: --skip-gen but {binPath} missing.");
                return 2;
            }
            if (!File.Exists(binPath))
            {
                Console.Error.WriteLine($"Phase 0: generating {opts.Runs} transcripts serially to {binPath} ...");
                TranscriptIO.GenerateAndSave(opts.Runs, binPath, idxPath, (done, total, e) =>
                {
                    if (done % 10 == 0 || done == total)
                    {
                        double pct = 100.0 * done / total;
                        TimeSpan eta = done > 0 ? TimeSpan.FromMilliseconds(e.TotalMilliseconds * (total - done) / done) : TimeSpan.Zero;
                        Console.Error.Write($"\r  transcripts {done}/{total} ({pct:F1}%)  elapsed={Cli.TimeFormat.Format(e)}  ETA={Cli.TimeFormat.Format(eta)}        ");
                    }
                });
                Console.Error.WriteLine();
            }
            else
            {
                Console.Error.WriteLine($"Phase 0: using existing transcripts at {binPath}");
            }
            string transcriptSha = TranscriptIO.ComputeFileHashHex(binPath);
            Console.Error.WriteLine($"  transcripts SHA-256: {transcriptSha}");

            // ---- Phase 1: throughput + entropy check ----
            Console.Error.WriteLine("Phase 1: throughput + RNG entropy cross-check ...");
            double throughputOpsPerSec = BigEvalPreflight.MeasureSha256Throughput();
            Console.Error.WriteLine($"  single-thread SHA-256: {throughputOpsPerSec:N0} ops/s");
            if (throughputOpsPerSec < 3_000_000) Console.Error.WriteLine("  WARNING: throughput below expected; SHA-NI may not be active.");
            int workerCount = opts.Workers ?? Math.Max(1, Environment.ProcessorCount - 1);
            double shannonH = BigEvalPreflight.MeasureParallelEntropy(workerCount);
            Console.Error.WriteLine($"  Shannon entropy across {workerCount} workers: H = {shannonH:F6} bit/byte (expected ≥ 7.99)");
            if (shannonH < 7.99) Console.Error.WriteLine("  WARNING: entropy below threshold; RNG may have worker correlation.");

            // ---- Phase 2: run attackers ----
            var startTime = DateTime.UtcNow;
            var startSw = Stopwatch.StartNew();

            var cts = new CancellationTokenSource();
            long totalShaEvals = 0;

            using var reader = TranscriptIO.OpenReader(binPath, idxPath);

            // Load ground-truth seeds are in-transcript. Each attacker just loads what it needs.

            var statsB1 = new BigEvalPerAttacker { Name = "B.1 Baseline Random" };
            var statsB2 = new BigEvalPerAttacker { Name = "B.2 Hash-First" };
            var statsB3 = new BigEvalPerAttacker { Name = "B.3 Structured (Tail)" };
            var statsB4 = new BigEvalPerAttacker { Name = "B.4 AES-Oracle (power)" };
            // B.7 default mode = Tail (least-probable-first) — matches the
            // protocol's CandMax filter direction (password vectors are atypical).
            // The Typical mode tested 2026-05-04 confirmed wrong-direction (0/300
            // structural) on break_demo.
            var b7Mode = Attackers.ProbabilisticVectorGenerator.Mode.Tail;
            var b7Probe = new Attackers.StatSortedEnumerationAttacker { Mode_ = b7Mode };
            var statsB7 = new BigEvalPerAttacker { Name = b7Probe.Name };

            // B.4 oracle aggregate
            long b4TPSum = 0, b4FPSum = 0;
            int b4RoundsMeasured = 0, b4MaxTP = 0, b4MaxFP = 0;
            var b4TP = new ConcurrentBag<int>();
            var b4FP = new ConcurrentBag<int>();
            long b4TotalQueries = 0;

            // B.2 per-round breakdown
            long b2TotalStageA = 0, b2TotalStageB = 0, b2TotalStageC = 0;
            int b2RoundsStageA = 0, b2RoundsStageB = 0, b2RoundsStageC = 0;

            // Shannon entropy during B.1 (separate from pre-check): collect worker-local byte counts
            var b1ByteCountsLocal = new ThreadLocal<long[]>(() => new long[256], trackAllValues: true);

            var progressLock = new object();
            Action<string, int, bool, int, int, long, double, string> appendProgress = (att, idx, succ, slotsM, slotsP, cands, sec, notes) =>
            {
                lock (progressLock)
                {
                    File.AppendAllText(opts.ProgressPath, string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8}\n",
                        DateTime.UtcNow.ToString("o"), att, idx, succ, slotsM, slotsP, cands, sec, CsvEscape(notes)));
                }
            };

            var resultsRows = new ConcurrentBag<string>(); // for main CSV

            bool TerminateOnSuccess(BigEvalPerAttacker aggStats, AttackerBase.Result r, int idx)
            {
                if (r.Success)
                {
                    EmitSuccessArtifact(aggStats.Name, idx, r);
                    Console.Error.WriteLine($"*** UNEXPECTED SUCCESS *** transcript={idx} attacker={aggStats.Name}. Halting pending workers.");
                    cts.Cancel();
                    return true;
                }
                return false;
            }

            var po = new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cts.Token };

            // ---- B.1 ----
            Console.Error.WriteLine($"[1/5] Running B.1 Baseline (budget={opts.B1BudgetSeconds}s per transcript, workers={workerCount}) ...");
            BigEvalAttackerRunner.RunPhase("B.1 Baseline Random", "B.1", reader, po, alreadyDone, statsB1, (i, t, slots) =>
            {
                var attacker = new BaselineRandomAttacker
                {
                    Budget = TimeSpan.FromSeconds(opts.B1BudgetSeconds),
                    ProgressInterval = long.MaxValue
                };
                var r = attacker.AttemptRecover(t);
                Interlocked.Add(ref totalShaEvals, r.CandidatesTried);

                lock (statsB1)
                {
                    statsB1.Runs++;
                    if (r.Success) statsB1.Successes++;
                    statsB1.TotalSlotsMatched += r.SlotsMatched;
                    statsB1.TotalSlotsPossible += slots;
                    statsB1.TotalCandidates += r.CandidatesTried;
                    statsB1.TotalSeconds += r.Elapsed.TotalSeconds;
                }

                appendProgress("B.1 Baseline Random", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds, r.Notes ?? "");
                resultsRows.Add(CsvResult("B.1", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds));

                TerminateOnSuccess(statsB1, r, i);
            });

            if (cts.IsCancellationRequested) return 4;

            // ---- B.2 ----
            Console.Error.WriteLine($"[2/5] Running B.2 Hash-First (pool={opts.B2B3Pool:N0}, workers={workerCount}) ...");
            BigEvalAttackerRunner.RunPhase("B.2 Hash-First", "B.2", reader, po, alreadyDone, statsB2, (i, t, slots) =>
            {
                var attacker = new HashFirstFilteredAttacker { PoolSize = opts.B2B3Pool };
                var r = attacker.AttemptRecover(t);
                Interlocked.Add(ref totalShaEvals, r.CandidatesTried);

                lock (statsB2)
                {
                    statsB2.Runs++;
                    if (r.Success) statsB2.Successes++;
                    statsB2.TotalSlotsMatched += r.SlotsMatched;
                    statsB2.TotalSlotsPossible += slots;
                    statsB2.TotalCandidates += r.CandidatesTried;
                    statsB2.TotalSeconds += r.Elapsed.TotalSeconds;

                    Interlocked.Add(ref b2TotalStageA, attacker.LastStats.TotalAtoBHits);
                    Interlocked.Add(ref b2TotalStageB, attacker.LastStats.TotalBtoAHits);
                    Interlocked.Add(ref b2TotalStageC, attacker.LastStats.TotalFinalHits);
                    foreach (var c in attacker.LastStats.AtoBSurvivorsPerRound) if (c > 0) Interlocked.Increment(ref b2RoundsStageA);
                    foreach (var c in attacker.LastStats.BtoASurvivorsPerRound) if (c > 0) Interlocked.Increment(ref b2RoundsStageB);
                    foreach (var c in attacker.LastStats.FinalCandidatesPerSlot) if (c > 0) Interlocked.Increment(ref b2RoundsStageC);
                }

                appendProgress("B.2 Hash-First", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds, r.Notes ?? "");
                resultsRows.Add(CsvResult("B.2", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds,
                    attacker.LastStats.TotalAtoBHits, attacker.LastStats.TotalBtoAHits, attacker.LastStats.TotalFinalHits));

                TerminateOnSuccess(statsB2, r, i);
            });

            if (cts.IsCancellationRequested) return 4;

            // ---- B.3 ----
            Console.Error.WriteLine($"[3/5] Running B.3 Structured Tail (pool={opts.B2B3Pool:N0}) ...");
            BigEvalAttackerRunner.RunPhase("B.3 Structured (Tail)", "B.3", reader, po, alreadyDone, statsB3, (i, t, slots) =>
            {
                var attacker = new StructuredSamplingAttacker
                {
                    Mode_ = StructuredSamplingAttacker.Mode.Tail,
                    PoolSize = opts.B2B3Pool,
                    Seed = opts.BaseSeed * 1_000_003 + i
                };
                var r = attacker.AttemptRecover(t);
                Interlocked.Add(ref totalShaEvals, r.CandidatesTried);

                lock (statsB3)
                {
                    statsB3.Runs++;
                    if (r.Success) statsB3.Successes++;
                    statsB3.TotalSlotsMatched += r.SlotsMatched;
                    statsB3.TotalSlotsPossible += slots;
                    statsB3.TotalCandidates += r.CandidatesTried;
                    statsB3.TotalSeconds += r.Elapsed.TotalSeconds;
                }

                appendProgress("B.3 Structured (Tail)", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds, r.Notes ?? "");
                resultsRows.Add(CsvResult("B.3", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds));

                TerminateOnSuccess(statsB3, r, i);
            });

            if (cts.IsCancellationRequested) return 4;

            // ---- B.4 ----
            Console.Error.WriteLine($"[4/5] Running B.4 AES-Oracle (K={opts.B4DiscSamples:N0}) ...");
            BigEvalAttackerRunner.RunPhase("B.4 AES-Oracle (power)", "B.4", reader, po, alreadyDone, statsB4, (i, t, slots) =>
            {
                var attacker = new AESOracleAttacker { DiscriminatorSamples = opts.B4DiscSamples };
                var r = attacker.AttemptRecover(t);
                Interlocked.Add(ref totalShaEvals, r.CandidatesTried);

                lock (statsB4)
                {
                    statsB4.Runs++;
                    statsB4.TotalSlotsPossible += slots;
                    statsB4.TotalCandidates += r.CandidatesTried;
                    statsB4.TotalSeconds += r.Elapsed.TotalSeconds;

                    Interlocked.Add(ref b4TPSum, attacker.LastDiag.TPAbsoluteHitsSum);
                    Interlocked.Add(ref b4FPSum, attacker.LastDiag.FPAbsoluteHitsSum);
                    Interlocked.Add(ref b4RoundsMeasured, attacker.LastDiag.RoundsMeasured);
                    if (attacker.LastDiag.MaxTP > b4MaxTP) b4MaxTP = attacker.LastDiag.MaxTP;
                    if (attacker.LastDiag.MaxFP > b4MaxFP) b4MaxFP = attacker.LastDiag.MaxFP;
                    Interlocked.Add(ref b4TotalQueries, (long)opts.B4DiscSamples * 2 * attacker.LastDiag.RoundsMeasured);
                    foreach (var m in attacker.LastDiag.Rounds)
                    {
                        b4TP.Add(m.TPHits);
                        b4FP.Add(m.FPHits);
                    }
                }

                appendProgress("B.4 AES-Oracle (power)", i, false, 0, slots, r.CandidatesTried, r.Elapsed.TotalSeconds, r.Notes ?? "");
                resultsRows.Add(CsvResult("B.4", i, false, 0, slots, r.CandidatesTried, r.Elapsed.TotalSeconds,
                    0, 0, 0, attacker.LastDiag.TPAbsoluteHitsSum, attacker.LastDiag.FPAbsoluteHitsSum));
            });

            if (cts.IsCancellationRequested) return 4;

            // ---- B.7 ----
            // B7Pool is separately configurable (--b7-pool). Default 1M bounds
            // per-worker dedup HashSet at ~30 MB. At 10M × 22 workers ~= 7 GB
            // is OOM risk; downscale --workers when raising --b7-pool.
            long b7Pool = opts.B7Pool;
            string b7Name = b7Probe.Name;
            Console.Error.WriteLine($"[5/5] Running {b7Name} (pool={b7Pool:N0}, workers={workerCount}; lattice exhaustion at break_demo ~1.7M, beyond fallback yields random samples) ...");
            BigEvalAttackerRunner.RunPhase(b7Name, "B.7", reader, po, alreadyDone, statsB7, (i, t, slots) =>
            {
                var attacker = new Attackers.StatSortedEnumerationAttacker
                {
                    Mode_ = b7Mode,
                    PoolSize = b7Pool
                };
                var r = attacker.AttemptRecover(t);
                Interlocked.Add(ref totalShaEvals, r.CandidatesTried);

                lock (statsB7)
                {
                    statsB7.Runs++;
                    if (r.Success) statsB7.Successes++;
                    statsB7.TotalSlotsMatched += r.SlotsMatched;
                    statsB7.TotalSlotsPossible += slots;
                    statsB7.TotalCandidates += r.CandidatesTried;
                    statsB7.TotalSeconds += r.Elapsed.TotalSeconds;
                }

                appendProgress(b7Name, i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds, r.Notes ?? "");
                resultsRows.Add(CsvResult("B.7", i, r.Success, r.SlotsMatched, slots, r.CandidatesTried, r.Elapsed.TotalSeconds,
                    attacker.LastStats?.TotalAtoBHits ?? 0, attacker.LastStats?.TotalBtoAHits ?? 0, attacker.LastStats?.TotalFinalHits ?? 0));

                TerminateOnSuccess(statsB7, r, i);
            });

            startSw.Stop();

            // ---- Aggregate B.4 ----
            double meanTP = b4RoundsMeasured > 0 ? (double)b4TPSum / b4RoundsMeasured : 0;
            double meanFP = b4RoundsMeasured > 0 ? (double)b4FPSum / b4RoundsMeasured : 0;
            double varTP = Variance(b4TP, meanTP);
            double varFP = Variance(b4FP, meanFP);
            double cohensD = (varTP + varFP) > 0 ? (meanTP - meanFP) / Math.Sqrt((varTP + varFP) / 2.0) : 0;
            double expectedFPperRound = (4096.0 / Math.Pow(2, 40)) * opts.B4DiscSamples;

            // ---- Write results CSV ----
            EnsureDir(opts.CsvPath);
            using (var sw = new StreamWriter(opts.CsvPath))
            {
                sw.WriteLine("attacker,transcript_index,success,slots_matched,slots_possible,candidates_tried,elapsed_seconds,stage_a_hits,stage_b_hits,stage_c_hits,tp_hits,fp_hits");
                foreach (var row in resultsRows.OrderBy(r => r)) sw.WriteLine(row);
            }

            // ---- Write markdown report ----
            var cfg = new BigEvalConfig
            {
                UtcStart = startTime.ToString("o"),
                UtcEnd = DateTime.UtcNow.ToString("o"),
                Runs = opts.Runs,
                B1Budget = TimeSpan.FromSeconds(opts.B1BudgetSeconds),
                B2B3Pool = opts.B2B3Pool,
                B4DiscSamples = opts.B4DiscSamples,
                Workers = workerCount,
                TranscriptFileSha = transcriptSha,
                SingleThreadSha256OpsPerSec = throughputOpsPerSec,
                ShannonEntropyB1 = shannonH,
                TotalShaEvaluations = totalShaEvals,
                Elapsed = startSw.Elapsed,
                VectorLength = Configuration.VectorLength,
                VectorCount = Configuration.VectorCount,
                HashLenAtoB = Configuration.HASHLength_AtoB,
                HashLenBtoA = Configuration.HASHLength_BtoA,
                HashLenPassword = Configuration.HASHLength_Password
            };
            var oracleAgg = new B4OracleAggregate
            {
                RoundsMeasured = b4RoundsMeasured,
                TPSum = b4TPSum,
                FPSum = b4FPSum,
                MeanTP = meanTP, MeanFP = meanFP,
                MaxTP = b4MaxTP, MaxFP = b4MaxFP,
                VarTP = varTP, VarFP = varFP,
                ExpectedFPPerRound = expectedFPperRound,
                CohensD = cohensD,
                TotalDiscriminatorQueries = b4TotalQueries
            };
            var b2Brk = new B2PerRoundBreakdown
            {
                TotalStageAHits = b2TotalStageA,
                TotalStageBHits = b2TotalStageB,
                TotalStageCHits = b2TotalStageC,
                RoundsWithStageAHits = b2RoundsStageA,
                RoundsWithStageBHits = b2RoundsStageB,
                RoundsWithStageCHits = b2RoundsStageC
            };
            BigEvalReport.WriteMarkdown(opts.ReportPath, new List<BigEvalPerAttacker> { statsB1, statsB2, statsB3, statsB4, statsB7 }, cfg, oracleAgg, b2Brk);

            // ---- Console summary ----
            Console.WriteLine();
            Console.WriteLine($"=== B.5-extended aggregate (elapsed {Cli.TimeFormat.Format(startSw.Elapsed)}) ===");
            foreach (var s in new[] { statsB1, statsB2, statsB3, statsB4, statsB7 })
            {
                Console.WriteLine($"  {s.Name,-28} runs={s.Runs,3}  success={s.Successes}/{s.Runs}  upper95CI={s.UpperCI95 * 100:F2}%  avg_time={s.AvgSeconds:F1}s  avg_samples={s.AvgCandidates:E2}");
            }
            Console.WriteLine($"  B.4 oracle: meanTP={meanTP:F6}, meanFP={meanFP:F6}, cohensD={cohensD:F3}, rounds={b4RoundsMeasured}");
            Console.WriteLine($"  Shannon H (B.1 pre-check): {shannonH:F6} bit/byte");
            Console.WriteLine();
            Console.WriteLine($"Report:   {opts.ReportPath}");
            Console.WriteLine($"CSV:      {opts.CsvPath}");
            Console.WriteLine($"Progress: {opts.ProgressPath}");
            return 0;
        }

        private static double Variance(ConcurrentBag<int> xs, double mean)
        {
            int n = xs.Count;
            if (n < 2) return 0;
            double s = 0;
            foreach (var x in xs) { double d = x - mean; s += d * d; }
            return s / (n - 1);
        }

        private static string CsvResult(string att, int idx, bool succ, int slotsM, int slotsP, long cands, double sec,
            long sa = 0, long sb = 0, long sc = 0, long tp = 0, long fp = 0)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                att, idx, succ, slotsM, slotsP, cands, sec, sa, sb, sc, tp, fp);
        }

        private static string CsvEscape(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void EmitSuccessArtifact(string attackerName, int idx, AttackerBase.Result r)
        {
            string fname = $"reports/big_eval_success_{Sanitize(attackerName)}_{idx}.json";
            EnsureDir(fname);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"timestamp_utc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"attacker\": \"{attackerName}\",");
            sb.AppendLine($"  \"transcript_index\": {idx},");
            sb.AppendLine($"  \"slots_matched\": {r.SlotsMatched},");
            sb.AppendLine($"  \"candidates_tried\": {r.CandidatesTried},");
            sb.AppendLine($"  \"elapsed_seconds\": {r.Elapsed.TotalSeconds:F3},");
            sb.AppendLine($"  \"recovered_k_hex\": \"{(r.RecoveredK == null ? "" : Convert.ToHexString(r.RecoveredK))}\",");
            sb.AppendLine($"  \"notes\": {JsonStr(r.Notes)}");
            sb.AppendLine("}");
            File.WriteAllText(fname, sb.ToString());
        }

        private static string Sanitize(string s)
        {
            var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
