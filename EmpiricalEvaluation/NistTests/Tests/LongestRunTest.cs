using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.4 — Test for the Longest Run of Ones in a Block.
    /// Block size M and category thresholds adapt to n per SP 800-22 table:
    ///   n ≥ 128     → M=8,     K=3
    ///   n ≥ 6272    → M=128,   K=5
    ///   n ≥ 750000  → M=10000, K=6
    /// </summary>
    public sealed class LongestRunTest : ITest
    {
        public string Name => "Longest Run of Ones in a Block";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            int M, K, N;
            double[] pi;
            int[] thresholds; // v <= thresholds[0], v == thresholds[0]+1, ..., v >= thresholds[^1]

            if (n < 128)
            {
                return new[] { TestResult.Make(Name, 0.0, "n too small (<128)") };
            }
            else if (n < 6272)
            {
                M = 8; K = 3;
                // categories: v<=1, 2, 3, v>=4
                pi = new[] { 0.2148, 0.3672, 0.2305, 0.1875 };
                thresholds = new[] { 1, 2, 3, 4 };
            }
            else if (n < 750000)
            {
                M = 128; K = 5;
                // categories: v<=4, 5, 6, 7, 8, v>=9
                pi = new[] { 0.1174, 0.2430, 0.2493, 0.1752, 0.1027, 0.1124 };
                thresholds = new[] { 4, 5, 6, 7, 8, 9 };
            }
            else
            {
                M = 10000; K = 6;
                // categories: v<=10, 11, 12, 13, 14, 15, v>=16
                pi = new[] { 0.0882, 0.2092, 0.2483, 0.1933, 0.1208, 0.0675, 0.0727 };
                thresholds = new[] { 10, 11, 12, 13, 14, 15, 16 };
            }

            N = n / M;
            int[] v = new int[pi.Length];
            for (int b = 0; b < N; b++)
            {
                int start = b * M;
                int longest = 0, cur = 0;
                for (int j = 0; j < M; j++)
                {
                    if (bits[start + j]) { cur++; if (cur > longest) longest = cur; }
                    else cur = 0;
                }

                // classify longest into category
                int cat;
                if (longest <= thresholds[0]) cat = 0;
                else if (longest >= thresholds[thresholds.Length - 1]) cat = pi.Length - 1;
                else cat = longest - thresholds[0]; // between first+1 and last-1
                v[cat]++;
            }

            double chiSq = 0;
            for (int i = 0; i < pi.Length; i++)
            {
                double expected = N * pi[i];
                double d = v[i] - expected;
                chiSq += d * d / expected;
            }

            double pValue = SpecialFunctions.GammaQ(K / 2.0, chiSq / 2.0);

            string detail = string.Format(CultureInfo.InvariantCulture,
                "M={0}, K={1}, N={2}, chi2={3:F4}", M, K, N, chiSq);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
