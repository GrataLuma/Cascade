using System;

namespace HashFirstAttacker.Reports
{
    /// <summary>
    /// Clopper-Pearson exact binomial confidence interval (one-sided upper bound).
    /// For k=0 uses closed form pU = 1 - (alpha/2)^(1/n); otherwise bisects the
    /// regularized incomplete beta function.
    /// Local LogGamma / RegularizedIncompleteBeta (Numerical Recipes §6.1-6.4) so
    /// HashFirstAttacker doesn't need a project reference to NistTests.
    /// </summary>
    public static class ClopperPearson
    {
        /// <summary>
        /// Two-sided 95% CI upper bound on the binomial proportion p given k successes
        /// in n trials (alpha/2 = 0.025 per tail, which is the standard Clopper-Pearson
        /// convention for a 95% confidence interval).
        /// </summary>
        public static double UpperCI(int k, int n, double alpha = 0.05)
        {
            if (n <= 0) throw new ArgumentException("n must be > 0");
            if (k < 0 || k > n) throw new ArgumentException("k must be in [0, n]");
            if (k == n) return 1.0;
            double halfAlpha = alpha / 2.0;
            if (k == 0) return 1.0 - Math.Pow(halfAlpha, 1.0 / n);

            // Upper bound solves: sum_{i=0..k} C(n,i) p^i (1-p)^(n-i) = alpha/2
            // Equivalent to: I_{1-p}(n-k, k+1) = alpha/2, solve for p by bisection.
            double target = halfAlpha;
            double lo = 0.0, hi = 1.0;
            for (int iter = 0; iter < 100; iter++)
            {
                double p = 0.5 * (lo + hi);
                double val = RegularizedIncompleteBeta(1.0 - p, n - k, k + 1);
                if (val > target) lo = p; else hi = p;
                if (hi - lo < 1e-10) break;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Regularized incomplete beta I_x(a, b) = B(x; a, b) / B(a, b).</summary>
        public static double RegularizedIncompleteBeta(double x, double a, double b)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b)
                                 + a * Math.Log(x) + b * Math.Log(1 - x));
            if (x < (a + 1) / (a + b + 2))
                return bt * BetaContinuedFraction(x, a, b) / a;
            return 1.0 - bt * BetaContinuedFraction(1 - x, b, a) / b;
        }

        private static double BetaContinuedFraction(double x, double a, double b)
        {
            const double eps = 3e-15;
            const double fpmin = 1e-300;
            int maxIt = 200;
            double qab = a + b, qap = a + 1, qam = a - 1;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < fpmin) d = fpmin;
            d = 1.0 / d;
            double h = d;
            for (int m = 1; m <= maxIt; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d;
                h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1.0) < eps) return h;
            }
            return h;
        }

        public static double LogGamma(double a)
        {
            if (a <= 0) throw new ArgumentException("a must be > 0");
            double[] cof = {
                76.18009172947146, -86.50532032941677, 24.01409824083091,
                -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
            };
            double x = a, y = a;
            double tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) { y += 1.0; ser += cof[j] / y; }
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        public static int RunSelfTest()
        {
            // Expected values use two-sided 95% Clopper-Pearson convention (alpha/2 per tail).
            // k=0 cases are exact closed-form: pU = 1 - (alpha/2)^(1/n).
            // k>=1 cases bisected against chi-square approximation (Wilson-Hilferty);
            // tolerance 1e-3 allows minor approximation mismatch in reference values.
            var cases = new (string name, double actual, double expected, double tol)[]
            {
                ("UpperCI(0, 300, 0.05)", UpperCI(0, 300, 0.05), 1.0 - Math.Pow(0.025, 1.0 / 300), 1e-10),
                ("UpperCI(0, 100, 0.05)", UpperCI(0, 100, 0.05), 1.0 - Math.Pow(0.025, 1.0 / 100), 1e-10),
                ("UpperCI(0, 10, 0.05)",  UpperCI(0, 10, 0.05),  1.0 - Math.Pow(0.025, 1.0 / 10),  1e-10),
                ("UpperCI(1, 300, 0.05)", UpperCI(1, 300, 0.05), 0.01858, 1e-3),
                ("UpperCI(5, 300, 0.05)", UpperCI(5, 300, 0.05), 0.03890, 1e-3),
                ("RegInBeta(0.5, 1, 1)",  RegularizedIncompleteBeta(0.5, 1, 1), 0.5,  1e-10),
                ("LogGamma(5)",           LogGamma(5),                          3.1780538303479458, 1e-10),
            };

            int pass = 0, fail = 0;
            Console.WriteLine("Clopper-Pearson self-tests:");
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,12}  {4}", "Case", "Actual", "Expected", "|Diff|", "Verdict");
            Console.WriteLine(new string('-', 84));
            foreach (var c in cases)
            {
                double diff = Math.Abs(c.actual - c.expected);
                bool ok = diff <= c.tol;
                Console.WriteLine("{0,-30} {1,14:F8} {2,14:F8} {3,12:E3}  {4}", c.name, c.actual, c.expected, diff, ok ? "PASS" : "FAIL");
                if (ok) pass++; else fail++;
            }
            Console.WriteLine(new string('-', 84));
            Console.WriteLine($"Total: {pass} pass, {fail} fail");
            return fail == 0 ? 0 : 1;
        }
    }
}
