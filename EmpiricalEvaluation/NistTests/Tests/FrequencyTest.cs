using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.1 — Frequency (Monobit) Test.
    /// Evaluates whether the proportion of ones in the sequence is close to 0.5.
    /// </summary>
    public sealed class FrequencyTest : ITest
    {
        public string Name => "Frequency (Monobit)";

        public TestResult[] Run(bool[] bits)
        {
            int n = bits.Length;
            long s = 0;
            for (int i = 0; i < n; i++) s += bits[i] ? 1 : -1;

            double sObs = Math.Abs(s) / Math.Sqrt(n);
            double pValue = SpecialFunctions.Erfc(sObs / Math.Sqrt(2.0));

            string detail = string.Format(CultureInfo.InvariantCulture,
                "n={0}, sum={1}, S_obs={2:F6}", n, s, sObs);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
