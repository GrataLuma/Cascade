using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.3 — Runs Test.
    /// Counts the total number of runs (uninterrupted sequences of identical bits).
    /// </summary>
    public sealed class RunsTest : ITest
    {
        public string Name => "Runs";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            int ones = 0;
            for (int i = 0; i < n; i++) if (bits[i]) ones++;
            double pi = (double)ones / n;

            double tau = 2.0 / Math.Sqrt(n);
            if (Math.Abs(pi - 0.5) >= tau)
            {
                string d = string.Format(CultureInfo.InvariantCulture, "prefailed monobit (pi={0:F6}, tau={1:F6})", pi, tau);
                return new[] { TestResult.Make(Name, 0.0, d) };
            }

            long v = 1;
            for (int i = 0; i < n - 1; i++) if (bits[i] != bits[i + 1]) v++;

            double num = Math.Abs(v - 2.0 * n * pi * (1 - pi));
            double den = 2.0 * Math.Sqrt(2.0 * n) * pi * (1 - pi);
            double pValue = SpecialFunctions.Erfc(num / den);

            string detail = string.Format(CultureInfo.InvariantCulture, "n={0}, pi={1:F6}, V={2}", n, pi, v);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
