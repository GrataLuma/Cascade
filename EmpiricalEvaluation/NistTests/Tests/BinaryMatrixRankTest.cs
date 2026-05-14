using System;
using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.5 — Binary Matrix Rank Test.
    /// Divides the bit stream into 32×32 disjoint matrices and checks rank distribution (GF(2)).
    /// Expected probabilities: P(rank=M)=0.2888, P(rank=M-1)=0.5776, P(rank&lt;M-1)=0.1336 for M=Q=32.
    /// </summary>
    public sealed class BinaryMatrixRankTest : ITest
    {
        public int M { get; set; } = 32;
        public int Q { get; set; } = 32;

        public string Name => $"Binary Matrix Rank (M={M}, Q={Q})";

        public TestResult[] Run(bool[] bits)
        {
            int bitsPerMatrix = M * Q;
            int N = bits.Length / bitsPerMatrix;
            if (N < 38)
            {
                return new[] { TestResult.MakeSkipped(Name, $"insufficient input length: N={N} matrices < 38 required ({bitsPerMatrix * 38} bits minimum)") };
            }

            int fm = 0, fm1 = 0;
            byte[,] mat = new byte[M, Q];
            for (int k = 0; k < N; k++)
            {
                int off = k * bitsPerMatrix;
                for (int i = 0; i < M; i++)
                    for (int j = 0; j < Q; j++)
                        mat[i, j] = (byte)(bits[off + i * Q + j] ? 1 : 0);

                int r = Rank(mat, M, Q);
                if (r == M) fm++;
                else if (r == M - 1) fm1++;
            }
            int rest = N - fm - fm1;

            // Expected for M=Q=32
            double pFm = 0.2888;
            double pFm1 = 0.5776;
            double pRest = 0.1336;

            double term1 = Pow2(fm - pFm * N) / (pFm * N);
            double term2 = Pow2(fm1 - pFm1 * N) / (pFm1 * N);
            double term3 = Pow2(rest - pRest * N) / (pRest * N);
            double chiSq = term1 + term2 + term3;
            double pValue = Math.Exp(-chiSq / 2.0);

            string detail = string.Format(CultureInfo.InvariantCulture,
                "N={0}, F_M={1}, F_(M-1)={2}, rest={3}, chi2={4:F4}", N, fm, fm1, rest, chiSq);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }

        private static double Pow2(double x) => x * x;

        /// <summary>Gaussian elimination over GF(2) — forward and backward — to compute rank.</summary>
        private static int Rank(byte[,] a, int m, int q)
        {
            // Copy matrix since we mutate it
            byte[,] mat = (byte[,])a.Clone();
            int rank = 0;
            int min = Math.Min(m, q);
            int row = 0;
            for (int col = 0; col < q && row < m; col++)
            {
                int pivot = -1;
                for (int i = row; i < m; i++)
                {
                    if (mat[i, col] == 1) { pivot = i; break; }
                }
                if (pivot == -1) continue;
                if (pivot != row)
                {
                    for (int j = 0; j < q; j++) { (mat[row, j], mat[pivot, j]) = (mat[pivot, j], mat[row, j]); }
                }
                for (int i = 0; i < m; i++)
                {
                    if (i != row && mat[i, col] == 1)
                    {
                        for (int j = col; j < q; j++) mat[i, j] ^= mat[row, j];
                    }
                }
                rank++;
                row++;
            }
            return rank;
        }
    }
}
