using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.7 — Non-overlapping Template Matching Test.
    /// Counts non-overlapping occurrences of a fixed m-bit aperiodic template B in N blocks.
    /// After a match, the scan advances by m positions (non-overlapping).
    /// Default: m=9 with template "000000001" (aperiodic).
    /// </summary>
    public sealed class NonOverlappingTemplateTest : ITest
    {
        public bool[] Template { get; set; } = new[] { false, false, false, false, false, false, false, false, true };
        public int Blocks { get; set; } = 8;

        public string Name => $"Non-overlapping Template (m={Template.Length}, B={TemplateString()})";

        private string TemplateString()
        {
            char[] c = new char[Template.Length];
            for (int i = 0; i < Template.Length; i++) c[i] = Template[i] ? '1' : '0';
            return new string(c);
        }

        public TestResult[] Run(bool[] bits)
        {
            int m = Template.Length;
            int N = Blocks;
            int M = bits.Length / N;
            if (M < m) return new[] { TestResult.Make(Name, 0.0, "block too short") };

            double mu = (double)(M - m + 1) / Math.Pow(2, m);
            double sigma2 = M * (1.0 / Math.Pow(2, m) - (2.0 * m - 1.0) / Math.Pow(2, 2 * m));

            double chiSq = 0;
            for (int j = 0; j < N; j++)
            {
                int start = j * M;
                int matches = 0;
                int i = 0;
                while (i <= M - m)
                {
                    bool match = true;
                    for (int k = 0; k < m; k++)
                    {
                        if (bits[start + i + k] != Template[k]) { match = false; break; }
                    }
                    if (match) { matches++; i += m; }
                    else i++;
                }
                double diff = matches - mu;
                chiSq += diff * diff / sigma2;
            }

            double pValue = SpecialFunctions.GammaQ(N / 2.0, chiSq / 2.0);

            string detail = string.Format(CultureInfo.InvariantCulture,
                "N={0}, M={1}, mu={2:F4}, sigma2={3:F4}, chi2={4:F4}", N, M, mu, sigma2, chiSq);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
