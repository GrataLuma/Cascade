using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HashFirstAttacker.Reports;

namespace HashFirstAttacker
{
    /// <summary>
    /// F2.d aggregator: reads a per-run convergence CSV emitted by
    /// <see cref="ConvergenceCommand"/> and produces a paper-grade markdown summary
    /// with success rate, Clopper-Pearson 95% upper CI on the failure rate,
    /// iteration-count percentiles, and per-run elapsed percentiles. Also computes
    /// a SHA-256 of the source CSV file for reviewer-side integrity verification.
    /// </summary>
    public static class ConvergenceSummaryCommand
    {
        public static int Run(string csvPath, string outputPath)
        {
            Console.Error.WriteLine($"convergence-summary: csv={csvPath}, output={outputPath}");

            var rows = ReadCsv(csvPath);
            if (rows.Count == 0)
            {
                Console.Error.WriteLine("ERROR: CSV contains no data rows.");
                return 1;
            }

            int n = rows.Count;
            int successes = rows.Count(r => r.Success);
            int failures = n - successes;
            int finalHits = rows.Count(r => r.FinalHit);
            int keysMatch = rows.Count(r => r.KeysMatch);

            double cpUpper = ClopperPearson.UpperCI(failures, n, 0.05);

            var iterArr = rows.Select(r => (double)r.Iterations).ToArray();
            var elapsedArr = rows.Select(r => r.ElapsedMs).ToArray();

            string csvHash = ComputeFileSha256Hex(csvPath);
            File.WriteAllText(outputPath + ".sha256", $"{csvHash}  {Path.GetFileName(csvPath)}\n");

            EnsureDir(outputPath);
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.AppendLine($"# Convergence summary — N={n.ToString("N0", inv)}");
            sb.AppendLine();
            sb.AppendLine($"- **Source CSV:** `{Path.GetFileName(csvPath)}` (SHA-256: `{csvHash}`)");
            sb.AppendLine($"- **Generated UTC:** {DateTime.UtcNow:o}");
            sb.AppendLine();
            sb.AppendLine("## Convergence");
            sb.AppendLine();
            sb.AppendLine($"- Final-hit rate : {finalHits.ToString("N0", inv)} / {n.ToString("N0", inv)} = **{(100.0 * finalHits / n).ToString("F4", inv)}%**");
            sb.AppendLine($"- Keys-match rate: {keysMatch.ToString("N0", inv)} / {n.ToString("N0", inv)} = **{(100.0 * keysMatch / n).ToString("F4", inv)}%**");
            sb.AppendLine($"- Combined success (FH ∧ KM): {successes.ToString("N0", inv)} / {n.ToString("N0", inv)} = **{(100.0 * successes / n).ToString("F4", inv)}%**");
            sb.AppendLine($"- Failures: {failures.ToString("N0", inv)}");
            sb.AppendLine();
            sb.AppendLine("## Failure-rate confidence");
            sb.AppendLine();
            sb.AppendLine($"- **Clopper-Pearson upper 95% CI on failure rate:** {cpUpper.ToString("E3", inv)} ({(cpUpper * 100).ToString("G4", inv)}%)");
            sb.AppendLine($"- Method: two-sided 95% CI (α/2 = 0.025 per tail), exact binomial.");
            if (failures == 0)
            {
                sb.AppendLine($"- Closed-form: 1 − 0.025^(1/N) for k = 0.");
            }
            sb.AppendLine();
            sb.AppendLine("## Iteration-count distribution (rounds-to-Final)");
            sb.AppendLine();
            sb.AppendLine("| Statistic | Value |");
            sb.AppendLine("|-----------|------:|");
            sb.AppendLine($"| min       | {iterArr.Min().ToString("F0", inv)} |");
            sb.AppendLine($"| p05       | {Percentile(iterArr, 0.05).ToString("F0", inv)} |");
            sb.AppendLine($"| median    | {Percentile(iterArr, 0.50).ToString("F0", inv)} |");
            sb.AppendLine($"| mean      | {iterArr.Average().ToString("F2", inv)} |");
            sb.AppendLine($"| p95       | {Percentile(iterArr, 0.95).ToString("F0", inv)} |");
            sb.AppendLine($"| p99       | {Percentile(iterArr, 0.99).ToString("F0", inv)} |");
            sb.AppendLine($"| max       | {iterArr.Max().ToString("F0", inv)} |");
            sb.AppendLine();
            sb.AppendLine("## Per-run elapsed time");
            sb.AppendLine();
            sb.AppendLine("| Statistic | Value (ms) |");
            sb.AppendLine("|-----------|-----------:|");
            sb.AppendLine($"| min       | {elapsedArr.Min().ToString("F2", inv)} |");
            sb.AppendLine($"| p05       | {Percentile(elapsedArr, 0.05).ToString("F2", inv)} |");
            sb.AppendLine($"| median    | {Percentile(elapsedArr, 0.50).ToString("F2", inv)} |");
            sb.AppendLine($"| mean      | {elapsedArr.Average().ToString("F2", inv)} |");
            sb.AppendLine($"| p95       | {Percentile(elapsedArr, 0.95).ToString("F2", inv)} |");
            sb.AppendLine($"| p99       | {Percentile(elapsedArr, 0.99).ToString("F2", inv)} |");
            sb.AppendLine($"| max       | {elapsedArr.Max().ToString("F2", inv)} |");
            sb.AppendLine();
            double totalSec = elapsedArr.Sum() / 1000.0;
            sb.AppendLine($"**Total per-run elapsed:** {totalSec.ToString("F1", inv)} s ({(totalSec / 60).ToString("F2", inv)} min)");
            sb.AppendLine();
            sb.AppendLine("## Reviewer verification");
            sb.AppendLine();
            sb.AppendLine($"To independently verify the source CSV is unmodified:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine($"sha256sum {Path.GetFileName(csvPath)}");
            sb.AppendLine($"# Expected: {csvHash}");
            sb.AppendLine("```");

            File.WriteAllText(outputPath, sb.ToString());

            Console.WriteLine();
            Console.WriteLine($"=== convergence-summary (N={n:N0}) ===");
            Console.WriteLine($"Successes        : {successes:N0} / {n:N0}  ({100.0 * successes / n:F4}%)");
            Console.WriteLine($"CP upper 95% CI  : {cpUpper:E3}  on failure rate");
            Console.WriteLine($"Iterations (med) : {Percentile(iterArr, 0.50):F0}");
            Console.WriteLine($"Elapsed (med)    : {Percentile(elapsedArr, 0.50):F2} ms");
            Console.WriteLine($"CSV SHA-256      : {csvHash}");
            Console.WriteLine($"Report           : {outputPath}");
            Console.WriteLine($"Hash sidecar     : {outputPath}.sha256");
            return 0;
        }

        private struct Row
        {
            public int RunIndex;
            public double ElapsedMs;
            public int Iterations;
            public bool FinalHit;
            public bool KeysMatch;
            public bool Success;
        }

        private static List<Row> ReadCsv(string path)
        {
            var rows = new List<Row>();
            using var reader = new StreamReader(path);
            string header = reader.ReadLine();
            if (header == null) return rows;
            // Expected header: run_index,start_utc,elapsed_ms,iterations,final_hit,keys_match,success,key_hex
            string line;
            int lineNo = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 8)
                    throw new FormatException($"CSV line {lineNo}: expected ≥8 columns, got {parts.Length}");
                rows.Add(new Row
                {
                    RunIndex = int.Parse(parts[0], CultureInfo.InvariantCulture),
                    ElapsedMs = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    Iterations = int.Parse(parts[3], CultureInfo.InvariantCulture),
                    FinalHit = parts[4] == "1",
                    KeysMatch = parts[5] == "1",
                    Success = parts[6] == "1",
                });
            }
            return rows;
        }

        private static string ComputeFileSha256Hex(string path)
        {
            using var stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
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
