using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// F4.c calibration harness.
    ///
    /// Two modes:
    ///   * Worker mode (default): runs the provided combo list sequentially in
    ///     the current process. Writes markdown and optionally JSON.
    ///   * Orchestrator mode (--workers N, N > 1): splits the combo list into
    ///     N batches, spawns child HashFirstAttacker processes (one per batch)
    ///     via Process.Start, waits for all to finish, merges their JSON
    ///     outputs into a single markdown report. Used to sidestep the static
    ///     Configuration.CutLimit / CandidateMax fields, which cannot be made
    ///     per-thread without a larger refactor (deferred to F9).
    ///
    /// Combo input precedence:
    ///   1. --combos "cut1:cand1,cut2:cand2,..." (explicit list)
    ///   2. --cut-values and --cand-values (cartesian product)
    ///   3. DefaultCombos() (4 representative combinations)
    /// </summary>
    public static class CalibrateF4Command
    {
        public sealed class Combo
        {
            public string Label { get; set; }
            public double CutLimit { get; set; }
            public double CandidateMax { get; set; }
        }

        public sealed class Row
        {
            public Combo Combo { get; set; }
            public int Runs { get; set; }
            public int FinalHit { get; set; }
            public int KeysMatch { get; set; }
            public double AvgIterations { get; set; }
            public double P50Iterations { get; set; }
            public double P95Iterations { get; set; }
            public double AvgPasswordProbLog2 { get; set; }
            public double AvgSkippedRounds { get; set; }
            public double AvgElapsedMs { get; set; }
            public double AvgFillRejections { get; set; }
            public double AvgFillSafetyTriggers { get; set; }
            public long PeakWorkingSetBytes { get; set; }
        }

        public sealed class Options
        {
            public int Runs = 200;
            public int Workers = 1;
            public int MaxIterations = 5000;
            public int EarlyAbortWindow = 50;
            public long MemoryGuardBytes = 2L * 1024 * 1024 * 1024; // 2 GB
            public string OutputMarkdown;
            public string OutputJson;
            public List<Combo> Combos;
            /// <summary>F9 bridge-pattern workaround: orchestrator must propagate
            /// the active --config path to each spawned worker, otherwise the
            /// worker's default resolver loads configs/reference.json regardless
            /// of what the parent loaded. Set to the original CLI --config value
            /// (or null if the parent resolved the fallback).</summary>
            public string ConfigPath;
        }

        public static int Run(Options opts)
        {
            if (opts.Combos == null || opts.Combos.Count == 0)
                opts.Combos = DefaultCombos();

            if (opts.Workers > 1 && opts.Combos.Count > 1 && string.IsNullOrEmpty(opts.OutputJson))
            {
                return RunOrchestrator(opts);
            }

            return RunSequential(opts);
        }

        // -----------------------------------------------------------------
        // Worker / single-process mode: runs combos in this process.
        // -----------------------------------------------------------------

        private static int RunSequential(Options opts)
        {
            Console.Error.WriteLine($"calibrate-f4 [worker]: runs={opts.Runs} per combo, combos={opts.Combos.Count}");

            double origCut = Configuration.CutLimitProbabilityLog2;
            double origCand = Configuration.CandidateMaxProbabilityLog2;

            var rows = new List<Row>();
            var totalSw = Stopwatch.StartNew();

            for (int c = 0; c < opts.Combos.Count; c++)
            {
                var combo = opts.Combos[c];
                Configuration.CutLimitProbabilityLog2 = combo.CutLimit;
                Configuration.CandidateMaxProbabilityLog2 = combo.CandidateMax;

                long memBefore = GC.GetTotalMemory(true);
                Console.Error.WriteLine($"\n[combo {c + 1}/{opts.Combos.Count}] {combo.Label}: cut={FmtThreshold(combo.CutLimit)}, cand={FmtThreshold(combo.CandidateMax)}; mem_before={memBefore / (1024 * 1024)} MB");

                int finalHit = 0, keysMatch = 0;
                int[] iters = new int[opts.Runs];
                double[] pwLogs = new double[opts.Runs];
                int[] skipped = new int[opts.Runs];
                double[] elapsedMs = new double[opts.Runs];
                int[] fillRej = new int[opts.Runs];
                int[] fillSafe = new int[opts.Runs];
                long peakWS = 0;
                var selfProc = Process.GetCurrentProcess();

                var comboSw = Stopwatch.StartNew();
                int completed = 0;
                Exception comboError = null;
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
                    if (outcome.FinalHit) finalHit++;
                    if (outcome.KeysMatch) keysMatch++;
                    completed++;

                    // Memory guard: abort this combo if the worker process RSS
                    // exceeds the configured cap. Re-read every 5 runs to keep
                    // overhead negligible.
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

                    // Force GC between runs: ClientA accumulates ~4096 entries per
                    // iter in its allVectors dict; non-converging runs at MaxIter
                    // can leave hundreds of MB of Vector objects. GC.Collect between
                    // protocol runs caps peak process memory.
                    if ((i % 5) == 4)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    if ((i + 1) % Math.Max(1, opts.Runs / 10) == 0 || i + 1 == opts.Runs)
                    {
                        Console.Error.Write($"\r    [{i + 1}/{opts.Runs}] fh={finalHit} km={keysMatch} elapsed={comboSw.Elapsed}  ");
                    }

                    // Early abort: after EarlyAbortWindow runs, if convergence is
                    // obviously broken (< 25%), stop wasting wall-clock on this combo.
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
                    comboError = ex;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"    EXCEPTION in combo {c + 1}: {ex.GetType().Name}: {ex.Message}");
                }
                comboSw.Stop();
                Console.Error.WriteLine();
                long memAfter = GC.GetTotalMemory(false);
                Console.Error.WriteLine($"    combo done: completed={completed} fh={finalHit} km={keysMatch} elapsed={comboSw.Elapsed} mem_after={memAfter / (1024 * 1024)} MB{(comboError != null ? " (partial due to exception)" : "")}");

                // Slice the trackers to actually-completed runs (early abort leaves zeros).
                var itersSlice = iters.Take(completed).ToArray();
                var pwLogsSlice = pwLogs.Take(completed).ToArray();
                var skippedSlice = skipped.Take(completed).ToArray();
                var elapsedSlice = elapsedMs.Take(completed).ToArray();
                var fillRejSlice = fillRej.Take(completed).ToArray();
                var fillSafeSlice = fillSafe.Take(completed).ToArray();
                var validPwLogs = pwLogsSlice.Where(x => !double.IsNaN(x)).ToArray();

                rows.Add(new Row
                {
                    Combo = combo,
                    Runs = completed,
                    FinalHit = finalHit,
                    KeysMatch = keysMatch,
                    AvgIterations = itersSlice.Length > 0 ? itersSlice.Average() : double.NaN,
                    P50Iterations = itersSlice.Length > 0 ? Percentile(itersSlice, 0.50) : double.NaN,
                    P95Iterations = itersSlice.Length > 0 ? Percentile(itersSlice, 0.95) : double.NaN,
                    AvgPasswordProbLog2 = validPwLogs.Length > 0 ? validPwLogs.Average() : double.NaN,
                    AvgSkippedRounds = skippedSlice.Length > 0 ? skippedSlice.Average() : double.NaN,
                    AvgElapsedMs = elapsedSlice.Length > 0 ? elapsedSlice.Average() : double.NaN,
                    AvgFillRejections = fillRejSlice.Length > 0 ? fillRejSlice.Average() : double.NaN,
                    AvgFillSafetyTriggers = fillSafeSlice.Length > 0 ? fillSafeSlice.Average() : double.NaN,
                    PeakWorkingSetBytes = peakWS
                });

                // Drop allVectors references from previous combo before next.
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Incremental JSON flush so partial results survive a later crash.
                if (!string.IsNullOrEmpty(opts.OutputJson))
                {
                    try
                    {
                        EnsureDir(opts.OutputJson);
                        File.WriteAllText(opts.OutputJson, JsonSerializer.Serialize(rows, JsonOptions));
                    }
                    catch (Exception ioEx)
                    {
                        Console.Error.WriteLine($"    WARN: incremental JSON flush failed: {ioEx.Message}");
                    }
                }
            }

            Configuration.CutLimitProbabilityLog2 = origCut;
            Configuration.CandidateMaxProbabilityLog2 = origCand;

            if (!string.IsNullOrEmpty(opts.OutputJson))
            {
                EnsureDir(opts.OutputJson);
                File.WriteAllText(opts.OutputJson, JsonSerializer.Serialize(rows, JsonOptions));
                Console.Error.WriteLine($"JSON written: {opts.OutputJson}");
            }

            if (!string.IsNullOrEmpty(opts.OutputMarkdown))
            {
                WriteReport(opts.OutputMarkdown, rows, opts.Runs, totalSw.Elapsed);
            }

            PrintConsoleSummary(rows, totalSw.Elapsed, opts.Runs, opts.OutputMarkdown);

            // Worker/sequential mode: non-convergence of individual combos is an
            // expected signal (that's what we're calibrating), not a process-level
            // failure. Return 0 if we produced a row for every combo, else 1.
            return rows.Count == opts.Combos.Count ? 0 : 1;
        }

        // -----------------------------------------------------------------
        // Orchestrator mode: spawn N worker processes, merge their JSON.
        // -----------------------------------------------------------------

        private static int RunOrchestrator(Options opts)
        {
            int w = Math.Min(opts.Workers, opts.Combos.Count);
            Console.Error.WriteLine($"calibrate-f4 [orchestrator]: runs={opts.Runs} per combo, combos={opts.Combos.Count}, workers={w}");

            // Split combos interleaved for balanced work (run-time per combo is similar).
            var batches = Enumerable.Range(0, w).Select(_ => new List<Combo>()).ToList();
            for (int i = 0; i < opts.Combos.Count; i++) batches[i % w].Add(opts.Combos[i]);

            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("ERROR: cannot determine own executable path; falling back to sequential.");
                opts.Workers = 1;
                return RunSequential(opts);
            }

            var jsonPaths = new string[w];
            var exitCodes = new int[w];
            var sw = Stopwatch.StartNew();

            Parallel.For(0, w, new ParallelOptions { MaxDegreeOfParallelism = w }, wi =>
            {
                var batch = batches[wi];
                if (batch.Count == 0) { exitCodes[wi] = 0; return; }

                var comboArg = string.Join(",", batch.Select(c => $"{FmtThresholdCli(c.CutLimit)}:{FmtThresholdCli(c.CandidateMax)}"));
                var jsonPath = Path.Combine(Path.GetTempPath(), $"calibrate_w{wi}_{Guid.NewGuid():N}.json");
                jsonPaths[wi] = jsonPath;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("calibrate-f4");
                if (!string.IsNullOrEmpty(opts.ConfigPath))
                {
                    psi.ArgumentList.Add("--config");
                    psi.ArgumentList.Add(opts.ConfigPath);
                }
                psi.ArgumentList.Add("--combos");
                psi.ArgumentList.Add(comboArg);
                psi.ArgumentList.Add("--runs");
                psi.ArgumentList.Add(opts.Runs.ToString());
                psi.ArgumentList.Add("--max-iter");
                psi.ArgumentList.Add(opts.MaxIterations.ToString());
                psi.ArgumentList.Add("--early-abort-window");
                psi.ArgumentList.Add(opts.EarlyAbortWindow.ToString());
                psi.ArgumentList.Add("--memory-guard-mb");
                psi.ArgumentList.Add((opts.MemoryGuardBytes / (1024L * 1024L)).ToString());
                psi.ArgumentList.Add("--output-json");
                psi.ArgumentList.Add(jsonPath);

                using var p = Process.Start(psi);
                var stderrTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string line;
                    while ((line = p.StandardError.ReadLine()) != null)
                    {
                        Console.Error.WriteLine($"[w{wi}] {line}");
                    }
                });
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                stderrTask.Wait();
                exitCodes[wi] = p.ExitCode;
                Console.Error.WriteLine($"[w{wi}] exited {p.ExitCode} after {sw.Elapsed} (batch size {batch.Count})");
            });

            sw.Stop();

            var allRows = new List<Row>();
            for (int i = 0; i < w; i++)
            {
                if (string.IsNullOrEmpty(jsonPaths[i])) continue;
                // Always attempt to read JSON (incremental writes happen per combo,
                // so partial results survive a worker crash).
                if (!File.Exists(jsonPaths[i]))
                {
                    Console.Error.WriteLine($"WARN: worker {i} produced no JSON (exit {exitCodes[i]})");
                    continue;
                }
                try
                {
                    var rows = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(jsonPaths[i]), JsonOptions);
                    if (rows != null) allRows.AddRange(rows);
                    if (exitCodes[i] != 0)
                        Console.Error.WriteLine($"NOTE: worker {i} exited {exitCodes[i]} but partial JSON was recovered ({rows?.Count ?? 0} rows)");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"WARN: failed to parse worker {i} JSON: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(jsonPaths[i]); } catch { }
                }
            }

            // Re-sort allRows to match the original combos ordering.
            var ordered = new List<Row>();
            foreach (var c in opts.Combos)
            {
                var found = allRows.FirstOrDefault(r =>
                    r.Combo != null &&
                    DoubleEq(r.Combo.CutLimit, c.CutLimit) &&
                    DoubleEq(r.Combo.CandidateMax, c.CandidateMax));
                if (found != null) ordered.Add(found);
            }

            if (!string.IsNullOrEmpty(opts.OutputMarkdown))
            {
                WriteReport(opts.OutputMarkdown, ordered, opts.Runs, sw.Elapsed);
            }

            PrintConsoleSummary(ordered, sw.Elapsed, opts.Runs, opts.OutputMarkdown);

            // Orchestrator OK iff every combo produced a row; per-combo convergence
            // is a calibration signal, not a process-level error.
            bool allOk = ordered.Count == opts.Combos.Count;
            return allOk ? 0 : 1;
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        public static List<Combo> CartesianCombos(double[] cutValues, double[] candValues)
        {
            var res = new List<Combo>();
            foreach (var cut in cutValues)
                foreach (var cand in candValues)
                {
                    res.Add(new Combo
                    {
                        Label = $"cut={FmtThreshold(cut)} cand={FmtThreshold(cand)}",
                        CutLimit = cut,
                        CandidateMax = cand
                    });
                }
            return res;
        }

        public static List<Combo> ParseComboList(string arg)
        {
            // Format: "cut1:cand1,cut2:cand2,..."
            var res = new List<Combo>();
            if (string.IsNullOrWhiteSpace(arg)) return res;
            foreach (var pair in arg.Split(','))
            {
                var parts = pair.Split(':');
                if (parts.Length != 2) throw new ArgumentException($"Bad combo spec '{pair}' — expected 'cut:cand'");
                double cut = ParseThresholdCli(parts[0]);
                double cand = ParseThresholdCli(parts[1]);
                res.Add(new Combo
                {
                    Label = $"cut={FmtThreshold(cut)} cand={FmtThreshold(cand)}",
                    CutLimit = cut,
                    CandidateMax = cand
                });
            }
            return res;
        }

        public static double[] ParseDoubleList(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return Array.Empty<double>();
            return arg.Split(',').Select(s => ParseThresholdCli(s.Trim())).ToArray();
        }

        private static double ParseThresholdCli(string s)
        {
            if (string.Equals(s, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "-inf", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "-infinity", StringComparison.OrdinalIgnoreCase))
                return double.NegativeInfinity;
            return double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FmtThresholdCli(double v) =>
            double.IsNegativeInfinity(v) ? "off" : v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        private static bool DoubleEq(double a, double b) =>
            (double.IsNegativeInfinity(a) && double.IsNegativeInfinity(b)) || a == b;

        private static List<Combo> DefaultCombos() => new List<Combo>
        {
            new Combo { Label = "baseline (disabled)",         CutLimit = double.NegativeInfinity, CandidateMax = double.NegativeInfinity },
            new Combo { Label = "looser (-4, -8)",             CutLimit = -4,                      CandidateMax = -8 },
            new Combo { Label = "defaults (-8, -16)",          CutLimit = -8,                      CandidateMax = -16 },
            new Combo { Label = "stricter (-16, -32)",         CutLimit = -16,                     CandidateMax = -32 },
        };

        private static string FmtThreshold(double v)
        {
            if (double.IsNegativeInfinity(v)) return "off";
            return v.ToString("F0");
        }

        private static double Percentile(int[] xs, double p)
        {
            var s = xs.OrderBy(x => x).ToArray();
            int k = Math.Min(s.Length - 1, Math.Max(0, (int)Math.Floor(p * (s.Length - 1))));
            return s[k];
        }

        private static void PrintConsoleSummary(List<Row> rows, TimeSpan elapsed, int runs, string reportPath)
        {
            Console.WriteLine();
            Console.WriteLine($"=== F4.c calibration summary (N={runs} per combo) ===");
            Console.WriteLine($"{"Combo",-36} {"Cut",6} {"Cand",7} {"FH/N",10} {"KM/N",10} {"iter(med)",10} {"avgPwLog2",11} {"skipped",8}");
            foreach (var r in rows)
            {
                Console.WriteLine($"{r.Combo.Label,-36} {FmtThreshold(r.Combo.CutLimit),6} {FmtThreshold(r.Combo.CandidateMax),7} {r.FinalHit,4}/{r.Runs,-4} {r.KeysMatch,4}/{r.Runs,-4} {r.P50Iterations,10:F0} {r.AvgPasswordProbLog2,11:F2} {r.AvgSkippedRounds,8:F2}");
            }
            Console.WriteLine($"Total elapsed: {elapsed}");
            if (!string.IsNullOrEmpty(reportPath))
                Console.WriteLine($"Report:        {reportPath}");
        }

        private static void WriteReport(string path, List<Row> rows, int runs, TimeSpan elapsed)
        {
            EnsureDir(path);
            var sb = new StringBuilder();
            sb.AppendLine("# F4.c — probability thresholds calibration");
            sb.AppendLine();
            sb.AppendLine($"**Runs per combination:** {runs}");
            sb.AppendLine($"**Protocol parameters:** VL={Configuration.VectorLength}, VC={Configuration.VectorCount}, H_AtoB={Configuration.HASHLength_AtoB}, H_BtoA={Configuration.HASHLength_BtoA}, H_PW={Configuration.HASHLength_Password}, M={Configuration.AESPassedVectorsCount}, NoUpdateLimit={Configuration.NoUpdateLimit}, TargetProbability={Configuration.TargetProbability}");
            sb.AppendLine($"**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)");
            sb.AppendLine($"**Generated UTC:** {DateTime.UtcNow:o}");
            sb.AppendLine($"**Total wall-clock:** {elapsed}");
            sb.AppendLine();
            sb.AppendLine("Threshold encoding: `off` = `double.NegativeInfinity` (filter disabled); any other value is the log2-probability threshold. `CutLimit` filters pool vectors in `ClientA.FillVectors`/`ClientB.FillVectors` (reject vectors whose Stat-log2-prob is ≥ threshold, i.e. too typical). `CandidateMax` filters collision vectors in `ClientB.ProcessMessageFromA` (same semantic, applied after sorting). A round is skipped when the post-filter passed-vector count drops below M; that count is reported as `skipped_avg`.");
            sb.AppendLine();
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine($"| Combo | CutLimit | CandidateMax | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | fill_rej_avg | fill_safe_avg | peak_WS_MB | ms/run |");
            sb.AppendLine($"|-------|:-------:|:------------:|:------------:|:-------------:|-----------:|----------:|----------:|------------:|------------:|-------------:|--------------:|-----------:|-------:|");
            foreach (var r in rows)
            {
                long peakMb = r.PeakWorkingSetBytes / (1024 * 1024);
                sb.AppendLine($"| {r.Combo.Label} | {FmtThreshold(r.Combo.CutLimit)} | {FmtThreshold(r.Combo.CandidateMax)} | {r.FinalHit} / {r.Runs} | {r.KeysMatch} / {r.Runs} | {r.AvgIterations:F2} | {r.P50Iterations:F0} | {r.P95Iterations:F0} | {r.AvgPasswordProbLog2:F2} | {r.AvgSkippedRounds:F2} | {r.AvgFillRejections:F1} | {r.AvgFillSafetyTriggers:F3} | {peakMb} | {r.AvgElapsedMs:F1} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Legend and interpretation");
            sb.AppendLine();
            sb.AppendLine("- **FinalHit / KeysMatch** — primary correctness check. 100% is required for any combination recommended as a protocol default.");
            sb.AppendLine("- **iter(mean)** — convergence cost. Stricter filters raise this because skipped rounds push the prop < TargetProbability termination later.");
            sb.AppendLine("- **avg_pw_log2** — security indicator: mean Stat-log2-probability of the password vectors that end up in the final round. More negative = rarer region of the distribution = stronger structural defence.");
            sb.AppendLine("- **skipped_avg** — operational signal that the CandidateMax filter is kicking: counts rounds where it reduced the post-filter pool below M and the round was dropped.");
            sb.AppendLine("- **fill_rej_avg** — CutLimit diagnostic: mean count of pool-vector rejections per protocol run in `ClientA.FillVectors` / `ClientB.FillVectors`. Zero = CutLimit threshold is too loose to reject anything on this configuration.");
            sb.AppendLine("- **fill_safe_avg** — CutLimit diagnostic: mean count of `CutLimitSafetyAttempts`-cap triggers per run (vectors accepted unfiltered after 1000 failed attempts). `> 0.1` signals the threshold is too strict for the pool distribution.");
            sb.AppendLine("- **peak_WS_MB** — observed peak WorkingSet of the current worker process during this combo. Sanity bound for the memory guard.");

            File.WriteAllText(path, sb.ToString());
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Required for non-converging combos: AvgPasswordProbLog2 is NaN when
            // no run hit Final, and Combo.CutLimit may be NegativeInfinity when
            // the filter is disabled. Strict JSON forbids both; the named-literal
            // handler emits them as "NaN" / "-Infinity" string literals, which
            // round-trip safely on deserialize.
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
    }
}
