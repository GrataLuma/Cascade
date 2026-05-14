using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// F10.a calibration of the vector-update probability p_upd.
    ///
    /// p_upd is the per-vector per-round probability that the update with the
    /// public random vector R is applied. Protocol code tests
    /// `SecureRandom.Instance.NextDouble() > Configuration.NoUpdateLimit` — if the
    /// draw exceeds NoUpdateLimit, the vector is updated. Therefore
    /// `p_upd = 1 - NoUpdateLimit`.
    ///
    /// This harness sweeps a set of p_upd values, sets NoUpdateLimit accordingly,
    /// and runs N ProtocolRunner invocations per value. Metrics include the F4.c
    /// diagnostics (iter stats, avg_pw_log2, skipped rounds, fill-rejection
    /// counters) plus F10-specific Stat distribution statistics (mean Shannon
    /// entropy and mean max-probability across ClientA's last Stat's 32 byte
    /// positions). The entropy/max-prob pair characterizes how far the Stat has
    /// drifted from uniform at the end of a run — the mechanism p_upd directly
    /// controls.
    ///
    /// Same safety guards as F4.c: --max-iter, early-abort after a window of
    /// runs with low convergence, 2 GB WorkingSet cap, GC between runs and
    /// between combos, incremental JSON flush, NaN/Infinity-tolerant serializer.
    /// Sequential single-process: 7 values × 200 runs is small enough that
    /// process-level parallelism is not worth the orchestration complexity here.
    /// </summary>
    public static class CalibratePUpdCommand
    {
        public sealed class Row
        {
            public double PUpd { get; set; }
            public double NoUpdateLimit { get; set; }
            public int Runs { get; set; }
            public int FinalHit { get; set; }
            public int KeysMatch { get; set; }
            public double AvgIterations { get; set; }
            public double P50Iterations { get; set; }
            public double P95Iterations { get; set; }
            public double AvgPasswordProbLog2 { get; set; }
            public double AvgSkippedRounds { get; set; }
            public double AvgFillRejections { get; set; }
            public double AvgFillSafetyTriggers { get; set; }
            public double AvgStatEntropy { get; set; }
            public double AvgStatMaxProb { get; set; }
            public double AvgElapsedMs { get; set; }
            public long PeakWorkingSetBytes { get; set; }
        }

        public sealed class Options
        {
            public int Runs = 200;
            public int MaxIterations = 5000;
            public int EarlyAbortWindow = 50;
            public long MemoryGuardBytes = 2L * 1024 * 1024 * 1024;
            public string OutputMarkdown;
            public string OutputJson;
            public double[] PUpdValues;

            /// <summary>Optional override of Configuration.CutLimitProbabilityLog2
            /// applied for the duration of the sweep. Null = keep existing default.
            /// Use double.NegativeInfinity to disable the filter.</summary>
            public double? CutLimitOverride;

            /// <summary>Optional override of Configuration.CandidateMaxProbabilityLog2.
            /// Null = keep existing default. Use double.NegativeInfinity to disable
            /// the filter (F10.d ablation).</summary>
            public double? CandidateMaxOverride;
        }

        public static int Run(Options opts)
        {
            if (opts.PUpdValues == null || opts.PUpdValues.Length == 0)
                opts.PUpdValues = new[] { 0.1, 0.25, 0.5, 0.75, 0.9, 0.95, 1.0 };

            Console.Error.WriteLine($"calibrate-p-upd: p_upd_values=[{string.Join(", ", opts.PUpdValues.Select(v => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}], runs={opts.Runs}");

            double origNoUpd = Configuration.NoUpdateLimit;
            double origCut = Configuration.CutLimitProbabilityLog2;
            double origCand = Configuration.CandidateMaxProbabilityLog2;
            if (opts.CutLimitOverride.HasValue) Configuration.CutLimitProbabilityLog2 = opts.CutLimitOverride.Value;
            if (opts.CandidateMaxOverride.HasValue) Configuration.CandidateMaxProbabilityLog2 = opts.CandidateMaxOverride.Value;

            Console.Error.WriteLine($"  F4 thresholds for this sweep: CutLimit={FmtOverride(Configuration.CutLimitProbabilityLog2)}, CandidateMax={FmtOverride(Configuration.CandidateMaxProbabilityLog2)}");

            var rows = new List<Row>();
            var totalSw = Stopwatch.StartNew();
            var selfProc = Process.GetCurrentProcess();

            for (int c = 0; c < opts.PUpdValues.Length; c++)
            {
                double pUpd = opts.PUpdValues[c];
                double noUpd = 1.0 - pUpd;
                Configuration.NoUpdateLimit = noUpd;

                long memBefore = GC.GetTotalMemory(true);
                Console.Error.WriteLine($"\n[combo {c + 1}/{opts.PUpdValues.Length}] p_upd={pUpd:F2} (NoUpdateLimit={noUpd:F2}); mem_before={memBefore / (1024 * 1024)} MB");

                int finalHit = 0, keysMatch = 0;
                int[] iters = new int[opts.Runs];
                double[] pwLogs = new double[opts.Runs];
                int[] skipped = new int[opts.Runs];
                double[] elapsedMs = new double[opts.Runs];
                int[] fillRej = new int[opts.Runs];
                int[] fillSafe = new int[opts.Runs];
                double[] statEnt = new double[opts.Runs];
                double[] statMax = new double[opts.Runs];
                long peakWS = 0;

                var comboSw = Stopwatch.StartNew();
                int completed = 0;
                try
                {
                    for (int i = 0; i < opts.Runs; i++)
                    {
                        var runSw = Stopwatch.StartNew();
                        var outcome = new ProtocolRunner { MaxIterations = opts.MaxIterations }.Run();
                        runSw.Stop();

                        iters[i] = outcome.Iterations;
                        skipped[i] = outcome.RoundsSkippedByCandidateFilter;
                        elapsedMs[i] = runSw.Elapsed.TotalMilliseconds;
                        pwLogs[i] = outcome.AvgPasswordProbLog2;
                        fillRej[i] = outcome.FillRejections;
                        fillSafe[i] = outcome.FillSafetyTriggers;
                        statEnt[i] = outcome.StatEntropyAvg;
                        statMax[i] = outcome.StatMaxProbAvg;
                        if (outcome.FinalHit) finalHit++;
                        if (outcome.KeysMatch) keysMatch++;
                        completed++;

                        if ((i % 5) == 0)
                        {
                            selfProc.Refresh();
                            long ws = selfProc.WorkingSet64;
                            if (ws > peakWS) peakWS = ws;
                            if (ws > opts.MemoryGuardBytes)
                            {
                                Console.Error.WriteLine();
                                Console.Error.WriteLine($"    MEMORY GUARD: WorkingSet={ws / (1024 * 1024)} MB > {opts.MemoryGuardBytes / (1024 * 1024)} MB cap — aborting combo at run {i + 1}");
                                break;
                            }
                        }

                        if ((i % 5) == 4)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }

                        if ((i + 1) % Math.Max(1, opts.Runs / 10) == 0 || i + 1 == opts.Runs)
                        {
                            Console.Error.Write($"\r    [{i + 1}/{opts.Runs}] fh={finalHit} km={keysMatch} elapsed={comboSw.Elapsed}  ");
                        }

                        if (completed >= opts.EarlyAbortWindow && finalHit * 4 < completed)
                        {
                            Console.Error.WriteLine();
                            Console.Error.WriteLine($"    early abort: {finalHit}/{completed} < 25% convergence — combo declared non-viable");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"    EXCEPTION in combo {c + 1}: {ex.GetType().Name}: {ex.Message}");
                }
                comboSw.Stop();
                Console.Error.WriteLine();
                long memAfter = GC.GetTotalMemory(false);
                Console.Error.WriteLine($"    combo done: completed={completed} fh={finalHit} km={keysMatch} elapsed={comboSw.Elapsed} mem_after={memAfter / (1024 * 1024)} MB peak_ws={peakWS / (1024 * 1024)} MB");

                var itersSlice = iters.Take(completed).ToArray();
                var pwLogsSlice = pwLogs.Take(completed).ToArray();
                var skippedSlice = skipped.Take(completed).ToArray();
                var elapsedSlice = elapsedMs.Take(completed).ToArray();
                var fillRejSlice = fillRej.Take(completed).ToArray();
                var fillSafeSlice = fillSafe.Take(completed).ToArray();
                var statEntSlice = statEnt.Take(completed).Where(x => !double.IsNaN(x)).ToArray();
                var statMaxSlice = statMax.Take(completed).Where(x => !double.IsNaN(x)).ToArray();
                var validPwLogs = pwLogsSlice.Where(x => !double.IsNaN(x)).ToArray();

                rows.Add(new Row
                {
                    PUpd = pUpd,
                    NoUpdateLimit = noUpd,
                    Runs = completed,
                    FinalHit = finalHit,
                    KeysMatch = keysMatch,
                    AvgIterations = itersSlice.Length > 0 ? itersSlice.Average() : double.NaN,
                    P50Iterations = itersSlice.Length > 0 ? Percentile(itersSlice, 0.50) : double.NaN,
                    P95Iterations = itersSlice.Length > 0 ? Percentile(itersSlice, 0.95) : double.NaN,
                    AvgPasswordProbLog2 = validPwLogs.Length > 0 ? validPwLogs.Average() : double.NaN,
                    AvgSkippedRounds = skippedSlice.Length > 0 ? skippedSlice.Average() : double.NaN,
                    AvgFillRejections = fillRejSlice.Length > 0 ? fillRejSlice.Average() : double.NaN,
                    AvgFillSafetyTriggers = fillSafeSlice.Length > 0 ? fillSafeSlice.Average() : double.NaN,
                    AvgStatEntropy = statEntSlice.Length > 0 ? statEntSlice.Average() : double.NaN,
                    AvgStatMaxProb = statMaxSlice.Length > 0 ? statMaxSlice.Average() : double.NaN,
                    AvgElapsedMs = elapsedSlice.Length > 0 ? elapsedSlice.Average() : double.NaN,
                    PeakWorkingSetBytes = peakWS
                });

                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (!string.IsNullOrEmpty(opts.OutputJson))
                {
                    try
                    {
                        EnsureDir(opts.OutputJson);
                        File.WriteAllText(opts.OutputJson, JsonSerializer.Serialize(rows, JsonOpts));
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"    WARN: incremental JSON flush failed: {ex.Message}");
                    }
                }
            }

            Configuration.NoUpdateLimit = origNoUpd;
            Configuration.CutLimitProbabilityLog2 = origCut;
            Configuration.CandidateMaxProbabilityLog2 = origCand;

            if (!string.IsNullOrEmpty(opts.OutputMarkdown))
            {
                WriteReport(opts.OutputMarkdown, rows, opts.Runs, totalSw.Elapsed);
            }

            Console.WriteLine();
            Console.WriteLine($"=== F10.a p_upd calibration summary (N={opts.Runs} per value) ===");
            Console.WriteLine($"{"p_upd",6} {"NoUpd",6} {"FH/N",10} {"KM/N",10} {"iter(p50)",9} {"avgPwLog2",11} {"fill_rej",9} {"fill_safe",10} {"stat_ent",9} {"stat_max",9}");
            foreach (var r in rows)
            {
                Console.WriteLine($"{r.PUpd,6:F2} {r.NoUpdateLimit,6:F2} {r.FinalHit,4}/{r.Runs,-4} {r.KeysMatch,4}/{r.Runs,-4} {r.P50Iterations,9:F0} {r.AvgPasswordProbLog2,11:F2} {r.AvgFillRejections,9:F1} {r.AvgFillSafetyTriggers,10:F3} {r.AvgStatEntropy,9:F3} {r.AvgStatMaxProb,9:F4}");
            }
            Console.WriteLine($"Total elapsed: {totalSw.Elapsed}");
            Console.WriteLine($"Report:        {opts.OutputMarkdown}");

            return rows.Count == opts.PUpdValues.Length ? 0 : 1;
        }

        public static double[] ParseDoubleList(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return Array.Empty<double>();
            return arg.Split(',').Select(s => double.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        public static double ParseThresholdOrOff(string arg)
        {
            if (string.Equals(arg, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-inf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-infinity", StringComparison.OrdinalIgnoreCase))
                return double.NegativeInfinity;
            return double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FmtOverride(double v) =>
            double.IsNegativeInfinity(v) ? "off" : v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        private static double Percentile(int[] xs, double p)
        {
            var s = xs.OrderBy(x => x).ToArray();
            int k = Math.Min(s.Length - 1, Math.Max(0, (int)Math.Floor(p * (s.Length - 1))));
            return s[k];
        }

        private static void WriteReport(string path, List<Row> rows, int runs, TimeSpan elapsed)
        {
            EnsureDir(path);
            var sb = new StringBuilder();
            sb.AppendLine("# F10.a — p_upd (vector update probability) calibration");
            sb.AppendLine();
            sb.AppendLine($"**Runs per p_upd value:** {runs}");
            sb.AppendLine($"**Protocol parameters:** VL={Configuration.VectorLength}, VC={Configuration.VectorCount}, H_AtoB={Configuration.HASHLength_AtoB}, H_BtoA={Configuration.HASHLength_BtoA}, H_PW={Configuration.HASHLength_Password}, M={Configuration.AESPassedVectorsCount}, TargetProbability={Configuration.TargetProbability}");
            sb.AppendLine($"**F4 thresholds for this sweep:** CutLimitProbabilityLog2={FmtOverride(Configuration.CutLimitProbabilityLog2)}, CandidateMaxProbabilityLog2={FmtOverride(Configuration.CandidateMaxProbabilityLog2)}");
            sb.AppendLine($"**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)");
            sb.AppendLine($"**Generated UTC:** {DateTime.UtcNow:o}");
            sb.AppendLine($"**Total wall-clock:** {elapsed}");
            sb.AppendLine();
            sb.AppendLine("Parameter mapping: `p_upd = 1 − NoUpdateLimit`. Protocol code branches on `SecureRandom.Instance.NextDouble() > NoUpdateLimit` — a draw above the threshold triggers the vector update, so probability-of-update equals `1 − NoUpdateLimit`.");
            sb.AppendLine();
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine($"| p_upd | NoUpdLimit | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | fill_rej_avg | fill_safe_avg | stat_entropy | stat_max_prob | peak_WS_MB | ms/run |");
            sb.AppendLine($"|-----:|:----------:|:------------:|:-------------:|----------:|----------:|----------:|------------:|------------:|-------------:|--------------:|-------------:|--------------:|-----------:|-------:|");
            foreach (var r in rows)
            {
                long peakMb = r.PeakWorkingSetBytes / (1024 * 1024);
                sb.AppendLine($"| {r.PUpd:F2} | {r.NoUpdateLimit:F2} | {r.FinalHit} / {r.Runs} | {r.KeysMatch} / {r.Runs} | {r.AvgIterations:F2} | {r.P50Iterations:F0} | {r.P95Iterations:F0} | {r.AvgPasswordProbLog2:F2} | {r.AvgSkippedRounds:F2} | {r.AvgFillRejections:F1} | {r.AvgFillSafetyTriggers:F3} | {r.AvgStatEntropy:F3} | {r.AvgStatMaxProb:F4} | {peakMb} | {r.AvgElapsedMs:F1} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Legend");
            sb.AppendLine();
            sb.AppendLine("- **p_upd** — per-vector per-round probability of applying the update with R. `p_upd = 1 − NoUpdateLimit`.");
            sb.AppendLine("- **FinalHit / KeysMatch** — primary correctness check. 100% expected for any viable default.");
            sb.AppendLine("- **iter(mean/p50/p95)** — convergence cost at this p_upd.");
            sb.AppendLine("- **avg_pw_log2** — security indicator: mean Stat-log2-probability of password vectors at Final.");
            sb.AppendLine("- **skipped_avg** — CandidateMax filter activity (rounds dropped because post-filter candidate count fell below M).");
            sb.AppendLine("- **fill_rej_avg / fill_safe_avg** — CutLimit filter activity in FillVectors (pool-refill rejection count / safety-cap triggers).");
            sb.AppendLine("- **stat_entropy** — mean Shannon entropy over ClientA's last Stat's 32 byte positions. Uniform = log2(256) = 8. Lower = more concentrated Stat = stronger drift.");
            sb.AppendLine("- **stat_max_prob** — mean max-probability per byte position in ClientA's last Stat. Uniform ≈ 0.0039 (=1/256); concentrated approaches 1.");
            sb.AppendLine("- **peak_WS_MB** — observed peak WorkingSet during this combo; bounded by the 2 GB memory guard.");

            File.WriteAllText(path, sb.ToString());
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
    }
}
