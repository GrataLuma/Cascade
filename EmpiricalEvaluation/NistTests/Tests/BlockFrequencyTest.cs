using System.Globalization;

namespace NistTests.Tests
{
    /// <summary>
    /// NIST SP 800-22 §2.2 — Frequency Test within a Block.
    /// Evaluates proportion of ones within M-bit non-overlapping blocks.
    /// </summary>
    public sealed class BlockFrequencyTest : ITest
    {
        public int BlockSize { get; set; } = 128;

        public string Name => $"Block Frequency (M={BlockSize})";

        public TestResult[] Run(bool[] bits)
        {
            int M = BlockSize;
            int N = bits.Length / M;
            double sum = 0;
            for (int i = 0; i < N; i++)
            {
                int ones = 0;
                int start = i * M;
                for (int j = 0; j < M; j++) if (bits[start + j]) ones++;
                double pi = (double)ones / M;
                double diff = pi - 0.5;
                sum += diff * diff;
            }
            double chiSq = 4.0 * M * sum;
            double pValue = SpecialFunctions.GammaQ(N / 2.0, chiSq / 2.0);

            string detail = string.Format(CultureInfo.InvariantCulture,
                "N={0}, M={1}, chi2={2:F4}", N, M, chiSq);
            return new[] { TestResult.Make(Name, pValue, detail) };
        }
    }
}
