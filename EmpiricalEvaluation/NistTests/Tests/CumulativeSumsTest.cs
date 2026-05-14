using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.11 — Cumulative Sums (Cusum) Test.
    /// Evaluates maximum excursion of the random walk formed from ±1 bits.
    /// Returns two results: forward (mode=0) and reverse (mode=1) walks.
    /// </summary>
    public sealed class CumulativeSumsTest : ITest
    {
        public string Name => "Cumulative Sums";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            var results = new TestResult[2];
            results[0] = Compute(bits, forward: true);
            results[1] = Compute(bits, forward: false);
            return results;
        }

        private static TestResult Compute(bool[] bits, bool forward)
        {
            int n = bits.Length;
            int sum = 0, z = 0;
            if (forward)
            {
                for (int i = 0; i < n; i++)
                {
                    sum += bits[i] ? 1 : -1;
                    int abs = Math.Abs(sum);
                    if (abs > z) z = abs;
                }
            }
            else
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    sum += bits[i] ? 1 : -1;
                    int abs = Math.Abs(sum);
                    if (abs > z) z = abs;
                }
            }

            double sqrtN = Math.Sqrt(n);
            double sum1 = 0.0, sum2 = 0.0;

            // NIST STS source uses integer cast (truncate toward zero), not floor
            int k1Min = (int)((-(double)n / z + 1.0) / 4.0);
            int k1Max = (int)(((double)n / z - 1.0) / 4.0);
            for (int k = k1Min; k <= k1Max; k++)
            {
                sum1 += NormalCdf((4.0 * k + 1.0) * z / sqrtN) - NormalCdf((4.0 * k - 1.0) * z / sqrtN);
            }

            int k2Min = (int)((-(double)n / z - 3.0) / 4.0);
            int k2Max = (int)(((double)n / z - 1.0) / 4.0);
            for (int k = k2Min; k <= k2Max; k++)
            {
                sum2 += NormalCdf((4.0 * k + 3.0) * z / sqrtN) - NormalCdf((4.0 * k + 1.0) * z / sqrtN);
            }

            double pValue = 1.0 - sum1 + sum2;
            if (pValue < 0) pValue = 0;
            if (pValue > 1) pValue = 1;

            string name = forward ? "Cumulative Sums (forward)" : "Cumulative Sums (reverse)";
            string detail = string.Format(CultureInfo.InvariantCulture, "n={0}, z={1}", n, z);
            return TestResult.Make(name, pValue, detail);
        }

        /// <summary>Standard normal CDF Φ(x).</summary>
        private static double NormalCdf(double x) => 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0)));

        private static double Erf(double x)
        {
            // 1 - erfc(x)
            if (x >= 0) return 1.0 - SpecialFunctions.Erfc(x);
            return -(1.0 - SpecialFunctions.Erfc(-x));
        }
    }
}
