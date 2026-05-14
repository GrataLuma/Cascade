using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.10 — Serial Test.
    /// Examines the frequencies of all overlapping m-bit patterns (circular augmentation).
    /// Returns two p-values: P1 for ∇ψ²_m and P2 for ∇²ψ²_m.
    /// </summary>
    public sealed class SerialTest : ITest
    {
        public int M { get; set; } = 3;

        public string Name => $"Serial (m={M})";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            int m = M;
            double psi2m = PsiSquared(bits, m, n);
            double psi2m1 = PsiSquared(bits, m - 1, n);
            double psi2m2 = m >= 2 ? PsiSquared(bits, m - 2, n) : 0.0;

            double del1 = psi2m - psi2m1;
            double del2 = psi2m - 2.0 * psi2m1 + psi2m2;

            double p1 = SpecialFunctions.GammaQ(Math.Pow(2, m - 2), del1 / 2.0);
            double p2 = SpecialFunctions.GammaQ(Math.Pow(2, m - 3), del2 / 2.0);

            string d1 = string.Format(CultureInfo.InvariantCulture,
                "psi2_m={0:F4}, psi2_m-1={1:F4}, del1={2:F4}", psi2m, psi2m1, del1);
            string d2 = string.Format(CultureInfo.InvariantCulture,
                "psi2_m-2={0:F4}, del2={1:F4}", psi2m2, del2);

            return new[]
            {
                TestResult.Make($"Serial P1 (m={m})", p1, d1),
                TestResult.Make($"Serial P2 (m={m})", p2, d2)
            };
        }

        private static double PsiSquared(bool[] bits, int m, int n)
        {
            if (m <= 0) return 0.0;
            int patterns = 1 << m;
            int[] count = new int[patterns];
            int mask = patterns - 1;

            int idx = 0;
            for (int i = 0; i < m; i++) idx = ((idx << 1) | (bits[i % n] ? 1 : 0)) & mask;
            count[idx]++;
            for (int i = 1; i < n; i++)
            {
                idx = ((idx << 1) | (bits[(i + m - 1) % n] ? 1 : 0)) & mask;
                count[idx]++;
            }

            double sum = 0;
            for (int i = 0; i < patterns; i++) sum += (double)count[i] * count[i];
            return (double)patterns / n * sum - n;
        }
    }
}
