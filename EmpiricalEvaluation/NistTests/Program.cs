using System;
using System.Collections.Generic;
using System.IO;
using GrataCascade.Core;

namespace NistTests
{
    public static class Program
    {
        /// <summary>F9.d: the config loaded for the current process.</summary>
        public static ProtocolConfiguration ActiveConfig;

        private static ProtocolConfiguration LoadCfg(Dictionary<string, string> parsed)
        {
            string explicitPath = parsed.TryGetValue("--config", out var cp) ? cp : null;
            var cfg = ConfigLoader.Load(explicitPath);
            ActiveConfig = cfg;
            Console.Error.WriteLine(
                $"Config: {cfg.OriginPath} (name={cfg.Name}, VL={cfg.VectorLength}, VC={cfg.VectorCount})");
            return cfg;
        }

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            try
            {
                switch (args[0])
                {
                    case "collect":
                        return RunCollect(args);
                    case "selftest":
                        return SpecialFunctionsTests.RunAll();
                    case "reftest":
                        return NistReferenceTests.RunAll();
                    case "run":
                        return RunBattery(args);
                    default:
                        Console.Error.WriteLine($"Unknown command: {args[0]}. Use --help.");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static int RunCollect(string[] args)
        {
            var parsed = ParseArgs(args);
            LoadCfg(parsed);
            var opts = new KeyCollector.Options
            {
                Runs = GetInt(parsed, "--runs", 1000),
                MaxIterations = GetInt(parsed, "--max-iter", Configuration.MaxIterations),
                Seed = parsed.TryGetValue("--seed", out var s) ? int.Parse(s) : (int?)null,
                SuppressProtocolOutput = !parsed.ContainsKey("--verbose-protocol")
            };
            string outPath = parsed.TryGetValue("--output", out var p) ? p : "reports/keys.bin";

            Console.Error.WriteLine($"Collecting {opts.Runs} keys (max-iter={opts.MaxIterations}, seed={opts.Seed?.ToString() ?? "none"})...");
            var collector = new KeyCollector();
            var result = collector.Collect(opts, (done, total, elapsed) =>
            {
                double pct = 100.0 * done / total;
                TimeSpan eta = done == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds * (total - done) / done);
                Console.Error.Write($"\r  {done}/{total} ({pct:F1}%)  elapsed={Format(elapsed)}  ETA={Format(eta)}    ");
            });
            Console.Error.WriteLine();

            KeyCollector.SaveToFile(outPath, result.Keys);
            string hex = KeyCollector.ComputeFileSha256Hex(outPath);
            long fileBytes = new FileInfo(outPath).Length;

            Console.WriteLine("=== KeyCollector A.1 Control Output ===");
            Console.WriteLine($"Runs requested        : {opts.Runs}");
            Console.WriteLine($"Successful            : {result.SuccessfulRuns}");
            Console.WriteLine($"Failed (pass mismatch): {result.FailedRuns}");
            Console.WriteLine($"No Final hit          : {result.NoFinalRuns}");
            Console.WriteLine($"Avg iterations / run  : {result.AvgIterationsPerRun}");
            Console.WriteLine($"Elapsed               : {Format(result.Elapsed)}");
            Console.WriteLine($"Avg time / run        : {result.AvgMsPerRun:F2} ms");
            Console.WriteLine($"Output file           : {outPath}  ({fileBytes} bytes)");
            Console.WriteLine($"SHA-256(file)         : {hex}");
            return 0;
        }

        private static int RunBattery(string[] args)
        {
            var parsed = ParseArgs(args);
            LoadCfg(parsed);
            int runs = GetInt(parsed, "--runs", 10000);
            int maxIter = GetInt(parsed, "--max-iter", Configuration.MaxIterations);
            int? seed = parsed.TryGetValue("--seed", out var s) ? int.Parse(s) : (int?)null;
            int blockSize = GetInt(parsed, "--block-size", 128);
            string keysFile = parsed.TryGetValue("--keys-file", out var kf) ? kf : null;
            string reportPath = parsed.TryGetValue("--output", out var op) ? op : "reports/nist_report.md";
            string csvPath = parsed.TryGetValue("--csv", out var cp) ? cp : "reports/nist_results.csv";

            byte[][] keys;
            int requested = runs;
            string source;
            if (!string.IsNullOrEmpty(keysFile) && System.IO.File.Exists(keysFile))
            {
                Console.Error.WriteLine($"Loading keys from {keysFile}...");
                keys = KeyCollector.LoadFromFile(keysFile);
                requested = keys.Length;
                source = keysFile;
            }
            else
            {
                Console.Error.WriteLine($"Collecting {runs} keys (max-iter={maxIter}, seed={seed?.ToString() ?? "none"})...");
                var collector = new KeyCollector();
                var result = collector.Collect(new KeyCollector.Options
                {
                    Runs = runs,
                    MaxIterations = maxIter,
                    Seed = seed
                }, (done, total, elapsed) =>
                {
                    double pct = 100.0 * done / total;
                    TimeSpan eta = done == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds * (total - done) / done);
                    Console.Error.Write($"\r  {done}/{total} ({pct:F1}%)  elapsed={Format(elapsed)}  ETA={Format(eta)}    ");
                });
                Console.Error.WriteLine();
                keys = result.Keys;
                source = $"fresh collection (seed={seed?.ToString() ?? "none"})";
                if (!string.IsNullOrEmpty(keysFile))
                {
                    KeyCollector.SaveToFile(keysFile, keys);
                    Console.Error.WriteLine($"Saved keys to {keysFile}");
                }
            }

            bool[] bits = NistTestRunner.BitsFromKeys(keys);
            int included = 0;
            foreach (var k in keys) if (k != null) included++;
            Console.Error.WriteLine($"Running NIST battery on {bits.Length} bits ({included} keys)...");

            var runner = new NistTestRunner { BlockSize = blockSize };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = runner.RunAll(bits);
            sw.Stop();

            var header = new ReportHeader
            {
                UtcDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
                Source = source,
                KeysRequested = requested,
                KeysIncluded = included,
                Bits = bits.Length,
                BlockSize = blockSize
            };
            ReportWriter.WriteMarkdown(reportPath, results, header,
                ConfigLoader.BuildReportHeader(ActiveConfig));
            ReportWriter.WriteCsv(csvPath, results);

            int pass = 0, fail = 0, skipped = 0;
            foreach (var r in results) {
                if (r.Skipped) skipped++;
                else if (r.Passed) pass++;
                else fail++;
            }

            Console.WriteLine();
            Console.WriteLine("=== NIST SP 800-22 Battery Results ===");
            Console.WriteLine($"Bits tested     : {bits.Length}");
            Console.WriteLine($"Keys included   : {included} / {requested}");
            Console.WriteLine($"Block size (M)  : {blockSize}");
            Console.WriteLine($"Tests elapsed   : {Format(sw.Elapsed)}");
            Console.WriteLine();
            Console.WriteLine($"{"Test",-42} {"p-value",12}  Verdict");
            Console.WriteLine(new string('-', 72));
            foreach (var r in results)
            {
                string verdict = r.Skipped ? "N/A" : (r.Passed ? "PASS" : "FAIL");
                string pStr = r.Skipped ? "     -      " : r.PValue.ToString("F6").PadLeft(12);
                Console.WriteLine($"{r.Name,-42} {pStr}  {verdict}");
            }
            Console.WriteLine(new string('-', 72));
            Console.WriteLine($"Total p-values: {results.Count}, PASS: {pass}, FAIL: {fail}, N/A: {skipped}");
            string overall = fail == 0
                ? (skipped == 0 ? "PASS ALL" : $"PASS ALL ({skipped} skipped, insufficient input)")
                : $"FAIL — {fail} test(s) failed";
            Console.WriteLine($"Overall: {overall}");
            Console.WriteLine();
            Console.WriteLine($"Report:   {reportPath}");
            Console.WriteLine($"CSV:      {csvPath}");
            return fail == 0 ? 0 : 3;
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--")) continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    d[a] = args[i + 1];
                    i++;
                }
                else
                {
                    d[a] = "true";
                }
            }
            return d;
        }

        private static int GetInt(Dictionary<string, string> d, string key, int def) => d.TryGetValue(key, out var v) ? int.Parse(v) : def;

        private static string Format(TimeSpan ts) => ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}m{ts.Seconds:D2}s" : ts.TotalMinutes >= 1 ? $"{ts.Minutes}m{ts.Seconds:D2}s" : $"{ts.TotalSeconds:F1}s";

        private static void PrintHelp()
        {
            Console.WriteLine("NistTests — NIST SP 800-22 statistical test battery for Grata Cascade");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project NistTests -- <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Global option (collect / run):");
            Console.WriteLine("  --config <path>    JSON config file (F9). If omitted, resolves configs/reference.json");
            Console.WriteLine("                     via: CWD -> exe dir -> walk up 5 parents. Fails if none found.");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  collect            Run the protocol N times, persist K* keys to binary file");
            Console.WriteLine("  selftest           Run SpecialFunctions unit tests");
            Console.WriteLine("  reftest            Run NIST reference-value tests (worked examples from SP 800-22)");
            Console.WriteLine("  run                Run the full NIST test battery (phase A.4, not yet implemented)");
            Console.WriteLine();
            Console.WriteLine("Options (collect):");
            Console.WriteLine("  --runs <N>         Number of protocol runs (default: 1000)");
            Console.WriteLine("  --max-iter <M>     Max iterations per run before giving up (default: from --config, reference v2: 500)");
            Console.WriteLine("  --seed <int>       Optional seed for protocol reproducibility");
            Console.WriteLine("  --output <path>    Output binary file (default: reports/keys.bin)");
            Console.WriteLine("  --verbose-protocol Do not suppress protocol's internal Console output");
            Console.WriteLine();
            Console.WriteLine("Options (run):");
            Console.WriteLine("  --runs <N>         Number of protocol runs (default: 10000)");
            Console.WriteLine("  --max-iter <M>     Max iterations per run (default: from --config, reference v2: 500)");
            Console.WriteLine("  --seed <int>       Optional seed for reproducibility");
            Console.WriteLine("  --keys-file <path> Load keys from file instead of collecting; if specified but missing, collect and save");
            Console.WriteLine("  --block-size <M>   Block size for block-based tests (default: 128)");
            Console.WriteLine("  --output <path>    Markdown report (default: reports/nist_report.md)");
            Console.WriteLine("  --csv <path>       CSV output (default: reports/nist_results.csv)");
            Console.WriteLine();
            Console.WriteLine("  --help, -h         Show this help");
        }
    }
}
