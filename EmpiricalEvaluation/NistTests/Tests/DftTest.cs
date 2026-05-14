using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.6 — Discrete Fourier Transform (Spectral) Test.
    /// Detects periodic features in the bit stream that would indicate deviation from
    /// randomness. Counts spectral peaks below the 95% confidence threshold.
    /// </summary>
    public sealed class DftTest : ITest
    {
        public string Name => "Discrete Fourier Transform (Spectral)";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = bits[i] ? 1.0 : -1.0;

            var S = Fft.Forward(x);
            int half = n / 2;
            double T = Math.Sqrt(Math.Log(1.0 / 0.05) * n); // threshold
            double N0 = 0.95 * half;                        // expected count below T (keep as double)
            int N1 = 0;
            for (int i = 0; i < half; i++) if (S[i].Magnitude < T) N1++;

            double d = (N1 - N0) / Math.Sqrt(n * 0.95 * 0.05 / 4.0);
            double pValue = SpecialFunctions.Erfc(Math.Abs(d) / Math.Sqrt(2.0));

            string detail = string.Format(CultureInfo.InvariantCulture,
                "n={0}, T={1:F4}, N0={2:F2}, N1={3}, d={4:F4}", n, T, N0, N1, d);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
