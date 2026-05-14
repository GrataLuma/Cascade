using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HashFirstAttacker
{
    /// <summary>
    /// F1.b convergence pilot — runs N ProtocolRunner invocations back-to-back
    /// under the current SecureRandom configuration and reports success rate,
    /// iteration-count distribution, and per-run elapsed statistics. Intended
    /// to re-validate the protocol after the F1 crypto-PRNG migration.
    ///
    /// F2 extension: per-run CSV emission (--csv) with periodic flush + per-run
    /// K* hex (transcript integrity hash). Used as the input artifact to
    /// `convergence-summary` for paper-grade aggregate reports with Clopper-Pearson CI.
    ///
    /// Post-F1 SecureRandom uses RandomNumberGenerator (unpredictable by design),
    /// so every invocation produces a different trajectory. No seed accepted.
    /// </summary>
    public static class ConvergenceCommand
    {
        public const string CsvHeader = "run_index,start_utc,elapsed_ms,iterations,final_hit,keys_match,success,key_hex";

        public static int Run(int runs, string outputPath, string csvPath = null, int flushEvery = 1000, int maxIterations = 5000)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = "reports/convergence_post_f1.md";

            Console.Error.WriteLine($"convergence: runs={runs}, output={outputPath}, csv={csvPath ?? "(none)"}, flushEvery={flushEvery}, maxIter={maxIterations}");

            int finalHit = 0;
            int keysMatch = 0;
            int[] iterations = new int[runs];
            double[] elapsedMs = new double[runs];

            StreamWriter csv = null;
            if (!string.IsNullOrEmpty(csvPath))
            {
                EnsureDir(csvPath);
                csv = new StreamWriter(csvPath, append: false);
                csv.WriteLine(CsvHeader);
            }

            var totalSw = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
            {
                var startUtc = DateTime.UtcNow;
                var sw = Stopwatch.StartNew();
                var outcome = new ProtocolRunner { MaxIterations = maxIterations }.Run();
                sw.Stop();

                iterations[i] = outcome.Iterations;
                elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
                if (outcome.FinalHit) finalHit++;
                if (outcome.KeysMatch) keysMatch++;

                if (csv != null)
                {
                    bool success = outcome.FinalHit && outcome.KeysMatch;
                    string keyHex = outcome.KeyA != null ? Convert.ToHexString(outcome.KeyA) : "";
                    string elapsedStr = sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);
                    csv.WriteLine($"{i},{startUtc:o},{elapsedStr},{outcome.Iterations},{(outcome.FinalHit ? 1 : 0)},{(outcome.KeysMatch ? 1 : 0)},{(success ? 1 : 0)},{keyHex}");
                    if ((i + 1) % flushEvery == 0) csv.Flush();
                }

                // F8 memory hygiene: drop the Transcript reference (largest object;
                // captures N=4096 hashes per round + ground-truth snapshots over up to
                // MaxIterations rounds) and force a Gen 2 GC every 10 runs. Without this
                // a 50-run sweep on L=16/N=4096/MaxIter=500 grew RSS to 14 GB before
                // the .NET runtime released it back. Per-iteration GC.Collect would be
                // overkill (synchronous full collection takes ~50-100 ms); every 10
                // strikes a balance between memory pressure and overhead.
                outcome.Transcript = null;
                outcome = null;
                if ((i + 1) % 10 == 0)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }

                if ((i + 1) % Math.Max(1, runs / 20) == 0 || i + 1 == runs)
                {
                    Console.Error.Write($"\r  [{i + 1}/{runs}] finalHit={finalHit} keysMatch={keysMatch} elapsed={totalSw.Elapsed}  ");
                }
            }
            totalSw.Stop();
            Console.Error.WriteLine();

            if (csv != null)
            {
                csv.Flush();
                csv.Dispose();
                Console.Error.WriteLine($"CSV: {csvPath} ({runs} rows)");
            }

            WriteReport(outputPath, runs, finalHit, keysMatch, iterations, elapsedMs, totalSw.Elapsed);

            Console.WriteLine();
            Console.WriteLine($"=== convergence (N={runs}) ===");
            Console.WriteLine($"Final hit         : {finalHit}/{runs}  ({100.0 * finalHit / runs:F2}%)");
            Console.WriteLine($"Keys match        : {keysMatch}/{runs}  ({100.0 * keysMatch / runs:F2}%)");
            Console.WriteLine($"Iterations (med)  : {Percentile(iterations, 0.50):F0}");
            Console.WriteLine($"Iterations (p95)  : {Percentile(iterations, 0.95):F0}");
            Console.WriteLine($"Elapsed/run (med) : {Percentile(elapsedMs, 0.50):F1} ms");
            Console.WriteLine($"Elapsed total     : {totalSw.Elapsed}");
            Console.WriteLine($"Report            : {outputPath}");

            // Non-zero exit only if the protocol regressed: at the reference config
            // we expect 100% convergence. Any failure is a material signal worth
            // failing CI on.
            return (finalHit == runs && keysMatch == runs) ? 0 : 1;
        }

        private static void WriteReport(
            string outputPath,
            int runs,
            int finalHit,
            int keysMatch,
            int[] iterations,
            double[] elapsedMs,
            TimeSpan totalElapsed)
        {
            EnsureDir(outputPath);
            var sb = new StringBuilder();
            sb.AppendLine("# Convergence pilot — post-F1 (crypto PRNG)");
            sb.AppendLine();
            sb.AppendLine($"**Runs:** {runs}");
            sb.AppendLine($"**Protocol parameters:** VL={GrataCascade.Core.Configuration.VectorLength}, VC={GrataCascade.Core.Configuration.VectorCount}, H_AtoB={GrataCascade.Core.Configuration.HASHLength_AtoB}, H_BtoA={GrataCascade.Core.Configuration.HASHLength_BtoA}, H_PW={GrataCascade.Core.Configuration.HASHLength_Password}, M={GrataCascade.Core.Configuration.AESPassedVectorsCount}");
            sb.AppendLine($"**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1 migration)");
            sb.AppendLine($"**Generated UTC:** {DateTime.UtcNow:o}");
            sb.AppendLine();
            sb.AppendLine("## Convergence");
            sb.AppendLine();
            sb.AppendLine($"- Final-hit rate : {finalHit}/{runs} = **{100.0 * finalHit / runs:F2}%**");
            sb.AppendLine($"- Keys-match rate: {keysMatch}/{runs} = **{100.0 * keysMatch / runs:F2}%**");
            sb.AppendLine();
            sb.AppendLine("Both rates are expected to be 100% on the reference configuration; any miss is a regression.");
            sb.AppendLine();
            sb.AppendLine("## Iteration-count distribution (rounds-to-Final)");
            sb.AppendLine();
            sb.AppendLine($"| Statistic | Value |");
            sb.AppendLine($"|-----------|------:|");
            sb.AppendLine($"| min       | {iterations.Min()} |");
            sb.AppendLine($"| p05       | {Percentile(iterations, 0.05):F0} |");
            sb.AppendLine($"| median    | {Percentile(iterations, 0.50):F0} |");
            sb.AppendLine($"| mean      | {iterations.Average():F2} |");
            sb.AppendLine($"| p95       | {Percentile(iterations, 0.95):F0} |");
            sb.AppendLine($"| p99       | {Percentile(iterations, 0.99):F0} |");
            sb.AppendLine($"| max       | {iterations.Max()} |");
            sb.AppendLine();
            sb.AppendLine("## Per-run elapsed");
            sb.AppendLine();
            sb.AppendLine($"| Statistic | Value (ms) |");
            sb.AppendLine($"|-----------|-----------:|");
            sb.AppendLine($"| min       | {elapsedMs.Min():F1} |");
            sb.AppendLine($"| median    | {Percentile(elapsedMs, 0.50):F1} |");
            sb.AppendLine($"| mean      | {elapsedMs.Average():F1} |");
            sb.AppendLine($"| p95       | {Percentile(elapsedMs, 0.95):F1} |");
            sb.AppendLine($"| max       | {elapsedMs.Max():F1} |");
            sb.AppendLine();
            sb.AppendLine($"**Total wall-clock:** {totalElapsed}");

            File.WriteAllText(outputPath, sb.ToString());
        }

        private static double Percentile(int[] xs, double p)
        {
            var s = xs.OrderBy(x => x).ToArray();
            int k = Math.Min(s.Length - 1, Math.Max(0, (int)Math.Floor(p * (s.Length - 1))));
            return s[k];
        }

        private static double Percentile(double[] xs, double p)
        {
            var s = xs.OrderBy(x => x).ToArray();
            int k = Math.Min(s.Length - 1, Math.Max(0, (int)Math.Floor(p * (s.Length - 1))));
            return s[k];
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
