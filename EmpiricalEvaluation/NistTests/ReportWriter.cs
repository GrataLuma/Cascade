using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NistTests.Tests;

namespace NistTests
{
    public static class ReportWriter
    {
        public static void WriteMarkdown(string path, List<TestResult> results, ReportHeader header, string configJsonBlock = null)
        {
            EnsureDir(path);
            var sb = new StringBuilder();
            sb.AppendLine("# NIST SP 800-22 Report — Grata Cascade");
            sb.AppendLine();
            sb.AppendLine($"- **Date (UTC):** {header.UtcDate}");
            sb.AppendLine($"- **Source:** {header.Source}");
            sb.AppendLine($"- **Keys included:** {header.KeysIncluded} / {header.KeysRequested}");
            sb.AppendLine($"- **Bitstream length:** {header.Bits} bits ({header.Bits / 8} bytes)");
            sb.AppendLine($"- **Block size (M):** {header.BlockSize}");
            sb.AppendLine($"- **Significance threshold:** α = 0.01");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(configJsonBlock))
            {
                sb.Append(configJsonBlock);
            }
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine("| Test | p-value | Verdict | Detail |");
            sb.AppendLine("|------|---------|---------|--------|");
            foreach (var r in results)
            {
                string pVal = r.Skipped ? "—" : r.PValue.ToString("F6", CultureInfo.InvariantCulture);
                string verdict = r.Skipped ? "N/A" : (r.Passed ? "PASS" : "FAIL");
                string detail = (r.Detail ?? "").Replace('|', '/');
                sb.AppendLine($"| {EscapeMd(r.Name)} | {pVal} | {verdict} | {detail} |");
            }
            sb.AppendLine();
            int pass = 0, fail = 0, skipped = 0;
            foreach (var r in results) {
                if (r.Skipped) skipped++;
                else if (r.Passed) pass++;
                else fail++;
            }
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Total: **{results.Count}** p-values");
            sb.AppendLine($"- PASS: **{pass}**");
            sb.AppendLine($"- FAIL: **{fail}**");
            if (skipped > 0) sb.AppendLine($"- N/A (skipped, insufficient input length): **{skipped}**");
            string overall = fail == 0
                ? (skipped == 0 ? "PASS ALL" : $"PASS ALL ({skipped} skipped — insufficient input)")
                : $"FAIL — {fail} test(s) below α=0.01";
            sb.AppendLine($"- Overall verdict: **{overall}**");
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine("- A test is counted separately for each p-value it emits (Cumulative Sums and Serial emit two).");
            sb.AppendLine("- PASS does not prove randomness; it means we cannot reject the uniformity hypothesis at α=0.01.");
            sb.AppendLine("- A single FAIL on 12 tests is expected ~12% of the time for truly random streams (type-I error at α=0.01 per test).");
            File.WriteAllText(path, sb.ToString());
        }

        public static void WriteCsv(string path, List<TestResult> results)
        {
            EnsureDir(path);
            var sb = new StringBuilder();
            sb.AppendLine("test_name,p_value,passed,detail");
            foreach (var r in results)
            {
                string name = EscapeCsv(r.Name);
                string p = r.PValue.ToString("G17", CultureInfo.InvariantCulture);
                string passed = r.Passed ? "true" : "false";
                string detail = EscapeCsv(r.Detail ?? "");
                sb.AppendLine($"{name},{p},{passed},{detail}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static string EscapeMd(string s) => s.Replace('|', '/');

        private static string EscapeCsv(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }

    public sealed class ReportHeader
    {
        public string UtcDate;
        public string Source;
        public int KeysRequested;
        public int KeysIncluded;
        public int Bits;
        public int BlockSize;
    }
}
