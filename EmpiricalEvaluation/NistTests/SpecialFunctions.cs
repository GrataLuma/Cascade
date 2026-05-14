using System;

namespace NistTests
{
    /// <summary>
    /// Numerical special functions needed by NIST SP 800-22 tests.
    /// References: Numerical Recipes in C (2nd ed.), Press/Teukolsky/Vetterling/Flannery; NIST SP 800-22 Appendix F.
    /// </summary>
    public static class SpecialFunctions
    {
        private const int GammaItMax = 200;
        private const double GammaEps = 1e-15;
        private const double GammaFpMin = 1e-300;

        /// <summary>
        /// Complementary error function erfc(x) = 1 - erf(x).
        /// Chebyshev rational approximation from Numerical Recipes §6.2 (errfc), relative accuracy ~1.2e-7.
        /// </summary>
        public static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 +
                    t * (1.00002368 +
                    t * (0.37409196 +
                    t * (0.09678418 +
                    t * (-0.18628806 +
                    t * (0.27886807 +
                    t * (-1.13520398 +
                    t * (1.48851587 +
                    t * (-0.82215223 +
                    t * 0.17087277))))))))); // Numerical Recipes §6.2
            return x >= 0.0 ? ans : 2.0 - ans;
        }

        /// <summary>
        /// Complementary standard normal CDF: P(Z > x) = 0.5 * erfc(x / sqrt(2)).
        /// Used by Monobit and Cumulative Sums tests (SP 800-22 §2.1, §2.11).
        /// </summary>
        public static double NormalCdfComplement(double x) => 0.5 * Erfc(x / Math.Sqrt(2.0));

        /// <summary>
        /// Regularized upper incomplete gamma Q(a, x) = Γ(a, x) / Γ(a).
        /// NIST uses igamc(a, x) = Q(a, x) for chi-square p-values (SP 800-22 Appendix F).
        /// Combines series representation for x &lt; a+1 and continued fraction for x ≥ a+1 (Numerical Recipes §6.2).
        /// </summary>
        public static double GammaQ(double a, double x)
        {
            if (x < 0.0 || a <= 0.0) throw new ArgumentException("GammaQ: a > 0 and x >= 0 required");
            if (x == 0.0) return 1.0;
            if (x < a + 1.0)
            {
                // series for P(a,x), return 1 - P
                return 1.0 - GammaSeriesP(a, x);
            }
            else
            {
                return GammaContinuedFractionQ(a, x);
            }
        }

        public static double GammaP(double a, double x) => 1.0 - GammaQ(a, x);

        private static double GammaSeriesP(double a, double x)
        {
            double ap = a;
            double sum = 1.0 / a;
            double del = sum;
            for (int n = 1; n <= GammaItMax; n++)
            {
                ap += 1.0;
                del *= x / ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * GammaEps)
                {
                    return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
                }
            }
            throw new InvalidOperationException("GammaSeriesP: max iterations exceeded (a=" + a + ", x=" + x + ")");
        }

        private static double GammaContinuedFractionQ(double a, double x)
        {
            // Lentz's method, Numerical Recipes §6.2
            double b = x + 1.0 - a;
            double c = 1.0 / GammaFpMin;
            double d = 1.0 / b;
            double h = d;
            for (int i = 1; i <= GammaItMax; i++)
            {
                double an = -i * (i - a);
                b += 2.0;
                d = an * d + b;
                if (Math.Abs(d) < GammaFpMin) d = GammaFpMin;
                c = b + an / c;
                if (Math.Abs(c) < GammaFpMin) c = GammaFpMin;
                d = 1.0 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1.0) < GammaEps)
                {
                    return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
                }
            }
            throw new InvalidOperationException("GammaContinuedFractionQ: max iterations exceeded (a=" + a + ", x=" + x + ")");
        }

        /// <summary>
        /// Natural log of the gamma function, Lanczos approximation g=5, n=6 (Numerical Recipes §6.1).
        /// Accurate to ~1e-10 for a > 0.
        /// </summary>
        public static double LogGamma(double a)
        {
            if (a <= 0.0) throw new ArgumentException("LogGamma: a > 0 required");
            double[] cof = {
                76.18009172947146, -86.50532032941677, 24.01409824083091,
                -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
            };
            double x = a;
            double y = a;
            double tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++)
            {
                y += 1.0;
                ser += cof[j] / y;
            }
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }
    }
}
