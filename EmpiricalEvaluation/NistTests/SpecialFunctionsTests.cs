using System;
using System.Collections.Generic;

namespace NistTests
{
    /// <summary>
    /// Self-contained unit tests for SpecialFunctions. Invoked via CLI: `NistTests selftest`.
    /// Reference values from Wolfram Alpha / Abramowitz & Stegun tables.
    /// Tolerance varies per function: Chebyshev Erfc ~1e-7, series/CF GammaQ ~1e-12.
    /// </summary>
    public static class SpecialFunctionsTests
    {
        private sealed class Case
        {
            public string Name;
            public double Actual;
            public double Expected;
            public double Tol;
        }

        public static int RunAll()
        {
            var cases = new List<Case>
            {
                // ---- Erfc (Chebyshev ~1e-7) ----
                new Case { Name = "Erfc(0)",           Actual = SpecialFunctions.Erfc(0.0),  Expected = 1.0,                    Tol = 1e-7 },
                new Case { Name = "Erfc(1.0)",         Actual = SpecialFunctions.Erfc(1.0),  Expected = 0.15729920705028513,    Tol = 1e-7 },
                new Case { Name = "Erfc(2.0)",         Actual = SpecialFunctions.Erfc(2.0),  Expected = 0.004677734981047266,   Tol = 1e-7 },
                new Case { Name = "Erfc(-1.0)",        Actual = SpecialFunctions.Erfc(-1.0), Expected = 1.8427007929497149,     Tol = 1e-7 },
                new Case { Name = "Erfc(0.5)",         Actual = SpecialFunctions.Erfc(0.5),  Expected = 0.4795001221869534,     Tol = 1e-7 },

                // ---- NormalCdfComplement ----
                new Case { Name = "NormCdfC(0)",       Actual = SpecialFunctions.NormalCdfComplement(0.0),  Expected = 0.5,                   Tol = 1e-7 },
                new Case { Name = "NormCdfC(1.96)",    Actual = SpecialFunctions.NormalCdfComplement(1.96), Expected = 0.024997895148220435,  Tol = 1e-7 },

                // ---- GammaQ (series/CF ~1e-12) ----
                new Case { Name = "GammaQ(0.5, 0.5)",  Actual = SpecialFunctions.GammaQ(0.5, 0.5), Expected = 0.31731050786291404, Tol = 1e-10 },
                new Case { Name = "GammaQ(1, 1)",      Actual = SpecialFunctions.GammaQ(1.0, 1.0), Expected = 0.36787944117144233, Tol = 1e-12 },
                new Case { Name = "GammaQ(0.5, 1)",    Actual = SpecialFunctions.GammaQ(0.5, 1.0), Expected = 0.15729920705028516, Tol = 1e-10 },
                new Case { Name = "GammaQ(2, 3)",      Actual = SpecialFunctions.GammaQ(2.0, 3.0), Expected = 0.19914827347145577, Tol = 1e-12 },
                new Case { Name = "GammaQ(5, 10)",     Actual = SpecialFunctions.GammaQ(5.0, 10.0),Expected = 0.029252688076961134, Tol = 1e-12 },

                // ---- LogGamma (Lanczos ~1e-10) ----
                new Case { Name = "LogGamma(1)",       Actual = SpecialFunctions.LogGamma(1.0),    Expected = 0.0,                  Tol = 1e-10 },
                new Case { Name = "LogGamma(2)",       Actual = SpecialFunctions.LogGamma(2.0),    Expected = 0.0,                  Tol = 1e-10 },
                new Case { Name = "LogGamma(5)",       Actual = SpecialFunctions.LogGamma(5.0),    Expected = 3.1780538303479458,   Tol = 1e-10 },
                new Case { Name = "LogGamma(0.5)",     Actual = SpecialFunctions.LogGamma(0.5),    Expected = 0.5723649429247001,   Tol = 1e-10 }
            };

            int pass = 0, fail = 0;
            Console.WriteLine("SpecialFunctions self-tests:");
            Console.WriteLine("{0,-25} {1,25} {2,25} {3,15}  {4}", "Case", "Actual", "Expected", "|Diff|", "Verdict");
            Console.WriteLine(new string('-', 110));
            foreach (var c in cases)
            {
                double diff = Math.Abs(c.Actual - c.Expected);
                bool ok = diff <= c.Tol || (double.IsNaN(c.Expected) && double.IsNaN(c.Actual));
                Console.WriteLine("{0,-25} {1,25:G17} {2,25:G17} {3,15:E3}  {4}  (tol={5:E1})",
                    c.Name, c.Actual, c.Expected, diff, ok ? "PASS" : "FAIL", c.Tol);
                if (ok) pass++; else fail++;
            }
            Console.WriteLine(new string('-', 110));
            Console.WriteLine($"Total: {pass} pass, {fail} fail ({cases.Count} cases)");
            return fail == 0 ? 0 : 1;
        }
    }
}
