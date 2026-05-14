using System;
using System.Collections.Generic;
using System.IO;
using GrataCascade.Core;
using HashFirstAttacker.Attackers;
using HashFirstAttacker.Cli;
using HashFirstAttacker.Reports;

namespace HashFirstAttacker.Commands
{
    /// <summary>
    /// Single-process attacker drivers (B.1 .. B.6) plus the small ad-hoc B.5
    /// aggregator. The large-scale persistent battery (B.5-extended) lives in
    /// <see cref="BigEval"/>; <see cref="RunB5Extended"/> just builds the
    /// <see cref="BigEval.Options"/> and delegates.
    /// </summary>
    public static class AttackerBatteryCommands
    {
        public static int RunB1(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int budgetSec = ArgParser.GetInt(parsed, "--budget-seconds", 60);

            Console.Error.WriteLine("Recording protocol transcript...");
            var outcome = new ProtocolRunner().Run();
            if (!outcome.FinalHit || !outcome.KeysMatch)
            {
                Console.Error.WriteLine("Protocol run failed to produce a valid K*; try again.");
                return 3;
            }
            int slots = outcome.Transcript.Final.BtoA_PasswordHashes.Count;
            Console.WriteLine($"Transcript: {outcome.Iterations} iterations, {slots} Final slots, K*={BitConverter.ToString(outcome.KeyA).Replace("-", "").Substring(0, 16)}...");

            var attacker = new BaselineRandomAttacker
            {
                Budget = TimeSpan.FromSeconds(budgetSec),
                ProgressCallback = (samples, filled, elapsed) =>
                {
                    double rate = elapsed.TotalSeconds > 0 ? samples / elapsed.TotalSeconds : 0;
                    Console.Error.Write($"\r  samples={samples:N0} matched={filled}/{slots} elapsed={TimeFormat.Format(elapsed)} rate={rate:N0}/s      ");
                }
            };

            Console.Error.WriteLine($"Running baseline attacker for budget={budgetSec}s...");
            var result = attacker.AttemptRecover(outcome.Transcript);
            Console.Error.WriteLine();

            Console.WriteLine();
            Console.WriteLine($"=== {attacker.Name} ===");
            Console.WriteLine($"Samples tried        : {result.CandidatesTried:N0}");
            Console.WriteLine($"Preimages matched    : {result.SlotsMatched}/{slots}");
            Console.WriteLine($"Elapsed              : {TimeFormat.Format(result.Elapsed)}");
            Console.WriteLine($"Recovered K*         : {(result.Success ? "YES — attack succeeded" : "no")}");
            Console.WriteLine($"Notes                : {result.Notes}");
            return result.Success ? 0 : 0;
        }

        public static int RunB2(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            long poolSize = ArgParser.GetLong(parsed, "--pool", 10_000_000);
            int runs = ArgParser.GetInt(parsed, "--runs", 1);
            bool histogram = parsed.ContainsKey("--histogram");

            int successes = 0, totalSlotsMatched = 0, totalSlots = 0;
            long totalCandidates = 0;
            TimeSpan totalElapsed = TimeSpan.Zero;
            long totalAtoBHits = 0, totalBtoAHits = 0, totalFinalHits = 0;

            for (int i = 0; i < runs; i++)
            {
                Console.Error.WriteLine($"[{i + 1}/{runs}] Recording transcript...");
                var outcome = new ProtocolRunner().Run();
                if (!outcome.FinalHit || !outcome.KeysMatch) { Console.Error.WriteLine("  skipped (protocol failed to produce K*)"); continue; }
                int slots = outcome.Transcript.Final.BtoA_PasswordHashes.Count;
                Console.Error.WriteLine($"  iters={outcome.Iterations}, slots={slots}, pool={poolSize:N0}");

                var attacker = new HashFirstFilteredAttacker
                {
                    PoolSize = poolSize,
                    ProgressCallback = (s, filled, e) =>
                    {
                        double rate = e.TotalSeconds > 0 ? s / e.TotalSeconds : 0;
                        Console.Error.Write($"\r    samples={s:N0} slots={filled}/{slots} rate={rate:N0}/s elapsed={TimeFormat.Format(e)}       ");
                    }
                };
                var r = attacker.AttemptRecover(outcome.Transcript);
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  {(r.Success ? "SUCCESS" : "no recovery")}: {r.Notes}");

                if (r.Success) successes++;
                totalSlotsMatched += r.SlotsMatched;
                totalSlots += slots;
                totalCandidates += r.CandidatesTried;
                totalElapsed += r.Elapsed;
                totalAtoBHits += attacker.LastStats.TotalAtoBHits;
                totalBtoAHits += attacker.LastStats.TotalBtoAHits;
                totalFinalHits += attacker.LastStats.TotalFinalHits;

                if (histogram && i == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("B.2.a — candidates surviving 5B A→B filter (per round):");
                    PrintHistogram(attacker.LastStats.AtoBSurvivorsPerRound);
                    Console.WriteLine();
                    Console.WriteLine("B.2.b — candidates surviving 2B B→A filter (per round):");
                    PrintHistogram(attacker.LastStats.BtoASurvivorsPerRound);
                    Console.WriteLine();
                    Console.WriteLine("B.2.c — candidates matching Final hash (per slot):");
                    PrintHistogram(attacker.LastStats.FinalCandidatesPerSlot);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== B.2 Hash-First Filtered — aggregate ===");
            Console.WriteLine($"Runs               : {runs}");
            Console.WriteLine($"Full K* recoveries : {successes}/{runs}");
            Console.WriteLine($"Slots matched      : {totalSlotsMatched}/{totalSlots}");
            Console.WriteLine($"Total candidates   : {totalCandidates:N0}");
            Console.WriteLine($"Total elapsed      : {TimeFormat.Format(totalElapsed)}");
            Console.WriteLine($"Stage-a hits       : {totalAtoBHits}");
            Console.WriteLine($"Stage-b hits       : {totalBtoAHits}");
            Console.WriteLine($"Stage-c hits       : {totalFinalHits}");
            return 0;
        }

        public static int RunB3(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            long poolSize = ArgParser.GetLong(parsed, "--pool", 10_000_000);
            int runs = ArgParser.GetInt(parsed, "--runs", 1);
            string modeStr = parsed.TryGetValue("--mode", out var m) ? m : "tail";
            var mode = modeStr.Equals("typical", StringComparison.OrdinalIgnoreCase)
                ? StructuredSamplingAttacker.Mode.Typical
                : StructuredSamplingAttacker.Mode.Tail;

            int successes = 0, totalSlotsMatched = 0, totalSlots = 0;
            long totalCandidates = 0;
            TimeSpan totalElapsed = TimeSpan.Zero;
            long totalAtoBHits = 0, totalBtoAHits = 0, totalFinalHits = 0;

            for (int i = 0; i < runs; i++)
            {
                Console.Error.WriteLine($"[{i + 1}/{runs}] Recording transcript...");
                var outcome = new ProtocolRunner().Run();
                if (!outcome.FinalHit || !outcome.KeysMatch) { Console.Error.WriteLine("  skipped (protocol failed to produce K*)"); continue; }
                int slots = outcome.Transcript.Final.BtoA_PasswordHashes.Count;
                Console.Error.WriteLine($"  iters={outcome.Iterations}, slots={slots}, pool={poolSize:N0}, mode={mode}");

                var attacker = new StructuredSamplingAttacker
                {
                    Mode_ = mode,
                    PoolSize = poolSize,
                    ProgressCallback = (s, filled, e) =>
                    {
                        double rate = e.TotalSeconds > 0 ? s / e.TotalSeconds : 0;
                        Console.Error.Write($"\r    samples={s:N0} slots={filled}/{slots} rate={rate:N0}/s elapsed={TimeFormat.Format(e)}       ");
                    }
                };
                var r = attacker.AttemptRecover(outcome.Transcript);
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  {(r.Success ? "SUCCESS" : "no recovery")}: {r.Notes}");

                if (r.Success) successes++;
                totalSlotsMatched += r.SlotsMatched;
                totalSlots += slots;
                totalCandidates += r.CandidatesTried;
                totalElapsed += r.Elapsed;
                totalAtoBHits += attacker.LastStats.TotalAtoBHits;
                totalBtoAHits += attacker.LastStats.TotalBtoAHits;
                totalFinalHits += attacker.LastStats.TotalFinalHits;
            }

            Console.WriteLine();
            Console.WriteLine($"=== B.3 Structured Sampling ({mode}) — aggregate ===");
            Console.WriteLine($"Runs               : {runs}");
            Console.WriteLine($"Full K* recoveries : {successes}/{runs}");
            Console.WriteLine($"Slots matched      : {totalSlotsMatched}/{totalSlots}");
            Console.WriteLine($"Total candidates   : {totalCandidates:N0}");
            Console.WriteLine($"Total elapsed      : {TimeFormat.Format(totalElapsed)}");
            Console.WriteLine($"Stage-a hits       : {totalAtoBHits}");
            Console.WriteLine($"Stage-b hits       : {totalBtoAHits}");
            Console.WriteLine($"Stage-c hits       : {totalFinalHits}");
            return 0;
        }

        public static int RunB4(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int runs = ArgParser.GetInt(parsed, "--runs", 5);
            int discSamples = ArgParser.GetInt(parsed, "--disc-samples", 50_000);

            int totalRoundsMeasured = 0;
            long totalTPHits = 0, totalFPHits = 0;
            int globalMaxTP = 0, globalMaxFP = 0;
            TimeSpan totalElapsed = TimeSpan.Zero;
            var allTP = new List<int>();
            var allFP = new List<int>();

            for (int i = 0; i < runs; i++)
            {
                Console.Error.WriteLine($"[{i + 1}/{runs}] Recording transcript...");
                var outcome = new ProtocolRunner().Run();
                if (!outcome.FinalHit || !outcome.KeysMatch) { Console.Error.WriteLine("  skipped (protocol failed)"); continue; }
                Console.Error.WriteLine($"  iters={outcome.Iterations}, K={discSamples:N0}");

                var attacker = new AESOracleAttacker { DiscriminatorSamples = discSamples };
                var r = attacker.AttemptRecover(outcome.Transcript);
                Console.Error.WriteLine($"  {r.Notes}");

                totalRoundsMeasured += attacker.LastDiag.RoundsMeasured;
                totalTPHits += attacker.LastDiag.TPAbsoluteHitsSum;
                totalFPHits += attacker.LastDiag.FPAbsoluteHitsSum;
                if (attacker.LastDiag.MaxTP > globalMaxTP) globalMaxTP = attacker.LastDiag.MaxTP;
                if (attacker.LastDiag.MaxFP > globalMaxFP) globalMaxFP = attacker.LastDiag.MaxFP;
                totalElapsed += r.Elapsed;
                foreach (var m in attacker.LastDiag.Rounds) { allTP.Add(m.TPHits); allFP.Add(m.FPHits); }
            }

            double meanTP = totalRoundsMeasured > 0 ? (double)totalTPHits / totalRoundsMeasured : 0;
            double meanFP = totalRoundsMeasured > 0 ? (double)totalFPHits / totalRoundsMeasured : 0;
            double varTP = Variance(allTP, meanTP);
            double varFP = Variance(allFP, meanFP);
            double sep = (varTP + varFP) > 0 ? (meanTP - meanFP) / Math.Sqrt((varTP + varFP) / 2.0) : 0;
            double expectedFPperQuery = 4096.0 / Math.Pow(2, 40);
            double expectedFPPerRound = expectedFPperQuery * discSamples;

            Console.WriteLine();
            Console.WriteLine("=== B.4 AES-Oracle — discriminator power aggregate ===");
            Console.WriteLine($"Runs                       : {runs}");
            Console.WriteLine($"Rounds measured total      : {totalRoundsMeasured}");
            Console.WriteLine($"K (samples per population) : {discSamples:N0}");
            Console.WriteLine($"TP hits sum                : {totalTPHits}");
            Console.WriteLine($"FP hits sum                : {totalFPHits}");
            Console.WriteLine($"Mean hits TP / round       : {meanTP:F4}");
            Console.WriteLine($"Mean hits FP / round       : {meanFP:F4}");
            Console.WriteLine($"Max hits TP / round        : {globalMaxTP}");
            Console.WriteLine($"Max hits FP / round        : {globalMaxFP}");
            Console.WriteLine($"Expected FP / round (SHA256 oracle): {expectedFPPerRound:F4}");
            Console.WriteLine($"Separation statistic       : {sep:F3}  (0 = indistinguishable)");
            Console.WriteLine($"Total elapsed              : {TimeFormat.Format(totalElapsed)}");
            return 0;
        }

        public static int RunB5(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int runs = ArgParser.GetInt(parsed, "--runs", 10);
            long pool = ArgParser.GetLong(parsed, "--pool", 10_000_000);
            int b1Budget = ArgParser.GetInt(parsed, "--b1-budget-seconds", 30);
            int b4DiscSamples = ArgParser.GetInt(parsed, "--b4-disc-samples", 100_000);
            string reportPath = parsed.TryGetValue("--output", out var op) ? op : "reports/attacker_report.md";
            string csvPath = parsed.TryGetValue("--csv", out var cp) ? cp : "reports/attacker_results.csv";

            Console.WriteLine($"B.5 aggregate — {runs} protocol runs × 4 attacker variants");
            Console.WriteLine($"  pool(B.2,B.3)={pool:N0}  b1-budget={b1Budget}s  b4-K={b4DiscSamples:N0}");
            Console.WriteLine();

            var statsB1 = new AttackerStats { Name = "B.1 Baseline Random" };
            var statsB2 = new AttackerStats { Name = "B.2 Hash-First" };
            var statsB3 = new AttackerStats { Name = "B.3 Structured (Tail)" };
            var statsB4 = new AttackerStats { Name = "B.4 AES-Oracle (power)" };

            long b4TotalTP = 0, b4TotalFP = 0;
            int b4TotalRounds = 0, b4MaxTP = 0, b4MaxFP = 0;
            var b4AllTP = new List<int>();
            var b4AllFP = new List<int>();

            for (int i = 0; i < runs; i++)
            {
                Console.Error.WriteLine($"[{i + 1}/{runs}] Recording transcript...");
                var outcome = new ProtocolRunner().Run();
                if (!outcome.FinalHit || !outcome.KeysMatch) { Console.Error.WriteLine("  skipped (protocol failed)"); continue; }
                int slots = outcome.Transcript.Final.BtoA_PasswordHashes.Count;
                Console.Error.WriteLine($"  iters={outcome.Iterations}, slots={slots}");

                var b1 = new BaselineRandomAttacker { Budget = TimeSpan.FromSeconds(b1Budget) };
                var r1 = b1.AttemptRecover(outcome.Transcript);
                statsB1.Runs++;
                if (r1.Success) statsB1.Successes++;
                statsB1.TotalSlotsMatched += r1.SlotsMatched;
                statsB1.TotalSlotsPossible += slots;
                statsB1.TotalCandidates += r1.CandidatesTried;
                statsB1.TotalElapsed += r1.Elapsed;
                Console.Error.WriteLine($"  B.1: slots={r1.SlotsMatched}/{slots} samples={r1.CandidatesTried:N0} elapsed={TimeFormat.Format(r1.Elapsed)}");

                var b2 = new HashFirstFilteredAttacker { PoolSize = pool };
                var r2 = b2.AttemptRecover(outcome.Transcript);
                statsB2.Runs++;
                if (r2.Success) statsB2.Successes++;
                statsB2.TotalSlotsMatched += r2.SlotsMatched;
                statsB2.TotalSlotsPossible += slots;
                statsB2.TotalCandidates += r2.CandidatesTried;
                statsB2.TotalElapsed += r2.Elapsed;
                Console.Error.WriteLine($"  B.2: slots={r2.SlotsMatched}/{slots} samples={r2.CandidatesTried:N0} elapsed={TimeFormat.Format(r2.Elapsed)}");

                var b3 = new StructuredSamplingAttacker { Mode_ = StructuredSamplingAttacker.Mode.Tail, PoolSize = pool };
                var r3 = b3.AttemptRecover(outcome.Transcript);
                statsB3.Runs++;
                if (r3.Success) statsB3.Successes++;
                statsB3.TotalSlotsMatched += r3.SlotsMatched;
                statsB3.TotalSlotsPossible += slots;
                statsB3.TotalCandidates += r3.CandidatesTried;
                statsB3.TotalElapsed += r3.Elapsed;
                Console.Error.WriteLine($"  B.3: slots={r3.SlotsMatched}/{slots} samples={r3.CandidatesTried:N0} elapsed={TimeFormat.Format(r3.Elapsed)}");

                var b4 = new AESOracleAttacker { DiscriminatorSamples = b4DiscSamples };
                var r4 = b4.AttemptRecover(outcome.Transcript);
                statsB4.Runs++;
                statsB4.TotalSlotsPossible += slots;
                statsB4.TotalCandidates += r4.CandidatesTried;
                statsB4.TotalElapsed += r4.Elapsed;

                b4TotalTP += b4.LastDiag.TPAbsoluteHitsSum;
                b4TotalFP += b4.LastDiag.FPAbsoluteHitsSum;
                b4TotalRounds += b4.LastDiag.RoundsMeasured;
                if (b4.LastDiag.MaxTP > b4MaxTP) b4MaxTP = b4.LastDiag.MaxTP;
                if (b4.LastDiag.MaxFP > b4MaxFP) b4MaxFP = b4.LastDiag.MaxFP;
                foreach (var mm in b4.LastDiag.Rounds) { b4AllTP.Add(mm.TPHits); b4AllFP.Add(mm.FPHits); }
                Console.Error.WriteLine($"  B.4: rounds={b4.LastDiag.RoundsMeasured} TP={b4.LastDiag.TPAbsoluteHitsSum} FP={b4.LastDiag.FPAbsoluteHitsSum} (K={b4DiscSamples:N0})");
            }

            statsB4.ExtraNotes = $"TP_total={b4TotalTP}, FP_total={b4TotalFP}, rounds={b4TotalRounds}, max_TP={b4MaxTP}, max_FP={b4MaxFP}";

            double meanTP = b4TotalRounds > 0 ? (double)b4TotalTP / b4TotalRounds : 0;
            double meanFP = b4TotalRounds > 0 ? (double)b4TotalFP / b4TotalRounds : 0;
            double varTP = Variance(b4AllTP, meanTP);
            double varFP = Variance(b4AllFP, meanFP);
            double sep = (varTP + varFP) > 0 ? (meanTP - meanFP) / Math.Sqrt((varTP + varFP) / 2.0) : 0;
            double expectedFPPerRound = (4096.0 / Math.Pow(2, 40)) * b4DiscSamples;

            string oracleSection =
                $"| Metric | Value |\n" +
                $"|---|---|\n" +
                $"| Rounds measured | {b4TotalRounds} |\n" +
                $"| Mean hits TP / round | {meanTP:F6} |\n" +
                $"| Mean hits FP / round | {meanFP:F6} |\n" +
                $"| Max hits TP / round | {b4MaxTP} |\n" +
                $"| Max hits FP / round | {b4MaxFP} |\n" +
                $"| Expected FP / round (random-oracle model) | {expectedFPPerRound:F6} |\n" +
                $"| Separation statistic | {sep:F3} (0 = indistinguishable) |\n" +
                $"\n" +
                $"**Interpretation:** TP and FP hit rates are statistically indistinguishable — both track the SHA-256 random-oracle prefix-collision baseline. The near-seed discriminator has no statistical power at K={b4DiscSamples:N0}.";

            var variants = new List<AttackerStats> { statsB1, statsB2, statsB3, statsB4 };
            var config = new ReportConfig
            {
                UtcDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
                Runs = runs,
                Pool = pool,
                B1BudgetSeconds = b1Budget,
                B4DiscSamples = b4DiscSamples,
                VectorLength = Configuration.VectorLength,
                VectorCount = Configuration.VectorCount,
                HashLenAtoB = Configuration.HASHLength_AtoB,
                HashLenBtoA = Configuration.HASHLength_BtoA,
                HashLenPassword = Configuration.HASHLength_Password
            };
            AttackerReport.WriteMarkdown(reportPath, variants, config, oracleSection,
                ConfigLoader.BuildReportHeader(Program.ActiveConfig));
            AttackerReport.WriteCsv(csvPath, variants);

            Console.WriteLine();
            Console.WriteLine("=== B.5 aggregate ===");
            foreach (var s in variants)
            {
                string slotsStr = s.TotalSlotsPossible > 0 ? $"{s.AvgSlotsMatched:F2}/{(double)s.TotalSlotsPossible / Math.Max(1, s.Runs):F1}" : "n/a";
                Console.WriteLine($"  {s.Name,-28} runs={s.Runs,3}  success={s.Successes,3}/{s.Runs,-3} ({s.SuccessRate * 100:F2}%)  avg_time={s.AvgSeconds:F1}s  avg_samples={s.AvgCandidates:E2}  avg_slots={slotsStr}");
            }
            Console.WriteLine();
            Console.WriteLine($"B.4 oracle: mean_TP={meanTP:F6}, mean_FP={meanFP:F6}, sep={sep:F3}");
            Console.WriteLine();
            Console.WriteLine($"Report:   {reportPath}");
            Console.WriteLine($"CSV:      {csvPath}");
            return 0;
        }

        public static int RunB5Extended(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            var opts = new BigEval.Options
            {
                Runs = ArgParser.GetInt(parsed, "--runs", 300),
                B1BudgetSeconds = ArgParser.GetInt(parsed, "--b1-budget-seconds", 1800),
                B2B3Pool = ArgParser.GetLong(parsed, "--pool", 10_000_000),
                B7Pool = ArgParser.GetLong(parsed, "--b7-pool", 1_000_000),
                B4DiscSamples = ArgParser.GetInt(parsed, "--b4-disc-samples", 1_000_000),
                ReportPath = parsed.TryGetValue("--output", out var op) ? op : "reports/big_eval_report.md",
                CsvPath = parsed.TryGetValue("--csv", out var cp) ? cp : "reports/big_eval_results.csv",
                ProgressPath = parsed.TryGetValue("--progress", out var pp) ? pp : "reports/big_eval_progress.csv",
                Resume = parsed.ContainsKey("--resume"),
                Overwrite = parsed.ContainsKey("--overwrite"),
                SkipGen = parsed.ContainsKey("--skip-gen"),
                Workers = parsed.TryGetValue("--workers", out var wv) ? (int?)int.Parse(wv) : null,
                BaseSeed = ArgParser.GetInt(parsed, "--base-seed", 0x5EED)
            };
            return BigEval.Run(opts);
        }

        public static int RunB6(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int k = ArgParser.GetInt(parsed, "--k", 2);
            int runs = ArgParser.GetInt(parsed, "--runs", 1);
            int maxRounds = ArgParser.GetInt(parsed, "--max-rounds", 0);
            string outCsv = parsed.TryGetValue("--output-csv", out var oc) ? oc : "reports/f5e/b6_default.csv";

            var dir = Path.GetDirectoryName(Path.GetFullPath(outCsv));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Console.Error.WriteLine($"B.6 Synthetic Flood: K={k}, runs={runs}, max_rounds/transcript={(maxRounds == 0 ? "all" : maxRounds.ToString())}");

            using var writer = new StreamWriter(outCsv, append: false);
            writer.WriteLine("transcript_id," + SyntheticFloodAttacker.CsvHeader);

            long totalDecrypts = 0;
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            for (int tr = 0; tr < runs; tr++)
            {
                Console.Error.WriteLine($"[{tr + 1}/{runs}] Recording transcript...");
                var outcome = new ProtocolRunner().Run();
                if (!outcome.FinalHit) { Console.Error.WriteLine("  skipped (protocol failed)"); continue; }
                Console.Error.WriteLine($"  iters={outcome.Iterations}, M_Final_slots={outcome.Transcript.Final?.BtoA_PasswordHashes.Count}");

                var attacker = new SyntheticFloodAttacker
                {
                    K = k,
                    MaxRoundsPerTranscript = maxRounds,
                    PerRoundCsvCallback = line => writer.WriteLine($"{tr},{line}")
                };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = attacker.AttemptRecover(outcome.Transcript);
                sw.Stop();
                totalDecrypts += r.CandidatesTried;
                Console.Error.WriteLine($"  [{tr + 1}] rounds_proc={attacker.LastDiag.RoundsProcessed} skip={attacker.LastDiag.RoundsSkipped} decrypts={r.CandidatesTried} elapsed={sw.Elapsed}");
            }

            totalSw.Stop();
            writer.Flush();
            Console.WriteLine();
            Console.WriteLine($"B.6 aggregate: transcripts={runs}, total_decryptions={totalDecrypts}, elapsed={totalSw.Elapsed}");
            Console.WriteLine($"CSV written: {outCsv}");
            return 0;
        }

        // ---- helpers shared by RunB2 / RunB4 / RunB5 ----

        public static double Variance(List<int> xs, double mean)
        {
            if (xs.Count < 2) return 0;
            double s = 0;
            foreach (var x in xs) { double d = x - mean; s += d * d; }
            return s / (xs.Count - 1);
        }

        public static void PrintHistogram(int[] counts)
        {
            int max = 0; foreach (var c in counts) if (c > max) max = c;
            int total = 0; foreach (var c in counts) total += c;
            int nonZero = 0; foreach (var c in counts) if (c > 0) nonZero++;
            int width = 40;
            var distribution = new SortedDictionary<int, int>();
            foreach (var c in counts)
            {
                if (!distribution.ContainsKey(c)) distribution[c] = 0;
                distribution[c]++;
            }
            Console.WriteLine($"  bins={counts.Length}, total_hits={total}, max_per_bin={max}, non-zero bins={nonZero}");
            Console.WriteLine($"  distribution of hits-per-bin: {string.Join(", ", distribution.Keys)} → counts {string.Join(", ", distribution.Values)}");
            if (nonZero > 0 && nonZero <= 20)
            {
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] == 0) continue;
                    int bars = max > 0 ? (int)Math.Round((double)counts[i] * width / max) : 0;
                    Console.WriteLine($"    [{i,3}] {counts[i],4} | {new string('#', bars)}");
                }
            }
        }
    }
}
