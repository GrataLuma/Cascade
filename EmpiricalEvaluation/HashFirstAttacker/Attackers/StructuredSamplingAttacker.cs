using System;
using GrataCascade.Core;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.3 — Structured Sampling Attacker.
    /// Reconstructs the protocol's byte-position probability distribution (Stat) from
    /// the publicly observable R vectors in the transcript, then biases candidate
    /// sampling toward either the tail (rare bytes — where the protocol chooses
    /// password vectors, per ClientB's sort-by-Probability-ascending) or the mode
    /// (typical bytes — protocol explicitly avoids these, so attack should fail).
    ///
    /// Internally this reuses HashFirstFilteredAttacker's pipeline with a custom
    /// Action&lt;byte[]&gt; sampler injected via SampleInto.
    /// </summary>
    public sealed class StructuredSamplingAttacker : AttackerBase
    {
        public enum Mode { Typical, Tail }

        public override string Name => $"B.3 Structured Sampling ({Mode_})";

        public Mode Mode_ { get; set; } = Mode.Tail;
        public long PoolSize { get; set; } = 10_000_000;
        public int? Seed { get; set; } = null;
        public Action<long, int, TimeSpan> ProgressCallback { get; set; }
        public long ProgressInterval { get; set; } = 500_000;

        public HashFirstFilteredAttacker.Stats LastStats { get; private set; }

        public override Result AttemptRecover(Transcript t)
        {
            // 1) Reconstruct Stat by replaying every observed R through StatItem.UpdateWithNumber.
            //    We use the protocol's own Stat implementation so the math stays identical.
            var stat = new Stat(t.VectorLength, "reconstructed");
            foreach (var round in t.Iterate)
            {
                if (round.R != null) stat.UpdateWithRandomVector(round.R);
            }

            // 2) Build 256-entry cumulative distribution per byte position.
            int[][] cum = new int[t.VectorLength][];
            for (int i = 0; i < t.VectorLength; i++)
            {
                double[] p = stat.StatItems[i].Probabilities;
                double[] w = new double[256];
                if (Mode_ == Mode.Typical)
                {
                    for (int j = 0; j < 256; j++) w[j] = Math.Max(0, p[j]);
                }
                else // Tail: invert (max - p[j]) — rare bytes get the most weight.
                {
                    double max = 0;
                    for (int j = 0; j < 256; j++) if (p[j] > max) max = p[j];
                    for (int j = 0; j < 256; j++) w[j] = Math.Max(0, max - p[j]);
                }

                double sum = 0;
                for (int j = 0; j < 256; j++) sum += w[j];
                if (sum <= 0) // degenerate: fall back to uniform so we don't get stuck
                {
                    for (int j = 0; j < 256; j++) w[j] = 1;
                    sum = 256;
                }

                cum[i] = new int[256];
                long running = 0;
                long target = 1L << 30; // use 2^30 to keep math in int range
                for (int j = 0; j < 256; j++)
                {
                    running += (long)(w[j] / sum * target);
                    cum[i][j] = (int)Math.Min(running, target);
                }
                cum[i][255] = (int)target; // ensure last bucket covers tail
            }

            // 3) Delegate pipeline to HashFirstFilteredAttacker with our sampler.
            var rng = Seed.HasValue ? new Random(Seed.Value) : new Random();
            int vl = t.VectorLength;
            Action<byte[]> sampler = buf =>
            {
                int target = 1 << 30;
                for (int i = 0; i < vl; i++)
                {
                    int u = rng.Next(target);
                    // binary search in cum[i] for smallest j with cum[i][j] > u
                    int lo = 0, hi = 255;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) >> 1;
                        if (cum[i][mid] > u) hi = mid;
                        else lo = mid + 1;
                    }
                    buf[i] = (byte)lo;
                }
            };

            var inner = new HashFirstFilteredAttacker
            {
                PoolSize = PoolSize,
                SampleInto = sampler,
                ProgressCallback = ProgressCallback,
                ProgressInterval = ProgressInterval
            };
            var res = inner.AttemptRecover(t);
            LastStats = inner.LastStats;
            res.Notes = $"mode={Mode_}, {res.Notes}";
            return res;
        }
    }
}
