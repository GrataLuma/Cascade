using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.8 — Approximate Entropy Test.
    /// Compares frequencies of overlapping m-bit and (m+1)-bit patterns in a circularly
    /// augmented bit sequence.
    /// </summary>
    public sealed class ApproximateEntropyTest : ITest
    {
        public int M { get; set; } = 2;

        public string Name => $"Approximate Entropy (m={M})";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            double phiM = Phi(bits, M, n);
            double phiM1 = Phi(bits, M + 1, n);
            double apEn = phiM - phiM1;
            double chiSq = 2.0 * n * (Math.Log(2.0) - apEn);
            double pValue = SpecialFunctions.GammaQ(Math.Pow(2, M - 1), chiSq / 2.0);

            string detail = string.Format(CultureInfo.InvariantCulture,
                "n={0}, phi(m)={1:F6}, phi(m+1)={2:F6}, ApEn={3:F6}, chi2={4:F4}", n, phiM, phiM1, apEn, chiSq);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }

        private static double Phi(bool[] bits, int m, int n)
        {
            int patterns = 1 << m;
            int[] count = new int[patterns];
            int mask = patterns - 1;

            int idx = 0;
            // Initialize first window using circular wrap
            for (int i = 0; i < m; i++)
            {
                idx = ((idx << 1) | (bits[i % n] ? 1 : 0)) & mask;
            }
            count[idx]++;
            for (int i = 1; i < n; i++)
            {
                idx = ((idx << 1) | (bits[(i + m - 1) % n] ? 1 : 0)) & mask;
                count[idx]++;
            }

            double phi = 0.0;
            for (int i = 0; i < patterns; i++)
            {
                if (count[i] > 0)
                {
                    double c = (double)count[i] / n;
                    phi += c * Math.Log(c);
                }
            }
            return phi;
        }
    }
}
