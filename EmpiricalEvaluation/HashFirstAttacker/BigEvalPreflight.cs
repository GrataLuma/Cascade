using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace HashFirstAttacker
{
    /// <summary>
    /// Phase-1 preflight diagnostics for <see cref="BigEval"/> runs:
    /// SHA-256 single-thread throughput probe and parallel-RNG byte-distribution
    /// entropy check. Results gate user-visible warnings about missing SHA-NI
    /// or worker-correlated randomness, but do not affect attack outcomes.
    /// </summary>
    public static class BigEvalPreflight
    {
        /// <summary>
        /// Measure single-thread SHA-256 throughput on a fixed 32-byte input.
        /// Used as a gate for the "SHA-NI may not be active" warning when
        /// throughput falls below ~3 Mops/s.
        /// </summary>
        public static double MeasureSha256Throughput()
        {
            var sw = Stopwatch.StartNew();
            long iters = 0;
            byte[] v = new byte[32];
            byte[] h = new byte[32];
            var rng = RandomNumberGenerator.Create();
            rng.GetBytes(v);
            while (sw.ElapsedMilliseconds < 5000)
            {
                for (int i = 0; i < 10000; i++) SHA256.HashData(v, h);
                iters += 10000;
            }
            sw.Stop();
            return iters / sw.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Aggregate Shannon entropy (bits per byte) across <paramref name="workers"/>
        /// independent <see cref="RandomNumberGenerator"/> instances drawing
        /// 2 MB each. Used to verify worker-RNGs are not correlated; expected
        /// value ≥ 7.99 bit/byte for an unbiased CSPRNG.
        /// </summary>
        public static double MeasureParallelEntropy(int workers)
        {
            const int bytesPerWorker = 2_000_000;
            long[] globalCounts = new long[256];
            Parallel.For(0, workers, w =>
            {
                long[] local = new long[256];
                byte[] buf = new byte[8192];
                var rng = RandomNumberGenerator.Create();
                long left = bytesPerWorker;
                while (left > 0)
                {
                    int take = (int)Math.Min(buf.Length, left);
                    rng.GetBytes(buf, 0, take);
                    for (int i = 0; i < take; i++) local[buf[i]]++;
                    left -= take;
                }
                lock (globalCounts) { for (int i = 0; i < 256; i++) globalCounts[i] += local[i]; }
            });
            long total = 0; foreach (var c in globalCounts) total += c;
            double H = 0;
            for (int i = 0; i < 256; i++)
            {
                if (globalCounts[i] == 0) continue;
                double p = (double)globalCounts[i] / total;
                H -= p * Math.Log2(p);
            }
            return H;
        }
    }
}
