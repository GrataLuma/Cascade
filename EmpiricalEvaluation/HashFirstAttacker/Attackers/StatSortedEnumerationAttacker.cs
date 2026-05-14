using System;
using System.Collections.Generic;
using System.Linq;
using GrataCascade.Core;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.7 — Stat-Sorted Enumeration Attacker.
    ///
    /// Reconstructs Stat from the public R sequence (same as B.3) but, instead of
    /// random sampling biased by Stat, deterministically enumerates candidate
    /// vectors in DECREASING order of joint Stat probability:
    ///
    ///     score(v) = product_i P_i[v[i]]   (or equivalently, sum of -log P_i[v[i]])
    ///
    /// Uses Lawler-style best-first lattice traversal (PriorityQueue + visited
    /// HashSet, dedup-on-enqueue, incremental cost update). The actual generator
    /// lives in <see cref="ProbabilisticVectorGenerator"/> below; this class is the
    /// AttackerBase wrapper that plugs the generator into the
    /// HashFirstFilteredAttacker hash-matching pipeline (same integration pattern
    /// as B.3).
    ///
    /// Limited to VectorLength &lt;= 8 (state packed into a long for HashSet dedup).
    /// For larger L the generator would need byte[]-keyed dedup; not implemented
    /// because all current research configs run with L &lt;= 8.
    /// </summary>
    public sealed class StatSortedEnumerationAttacker : AttackerBase
    {
        public override string Name => Mode_ == ProbabilisticVectorGenerator.Mode.Tail
            ? "B.7 Stat-Sorted Enum (Tail)"
            : "B.7 Stat-Sorted Enum (Typical)";

        /// <summary>
        /// Enumeration direction. Typical = highest joint Stat-probability first
        /// (every byte at its locally-most-frequent value). Tail = lowest joint
        /// probability first (every byte at its locally-rarest value). Tail is
        /// the right direction against this protocol because ClientB's CandMax
        /// filter selects atypical (low Stat-probability) vectors for password
        /// derivation; Typical-first enumeration structurally cannot hit h_P
        /// prefixes, as confirmed empirically against break_demo (0/300 K*).
        /// </summary>
        public ProbabilisticVectorGenerator.Mode Mode_ { get; set; }
            = ProbabilisticVectorGenerator.Mode.Tail;

        public long PoolSize { get; set; } = 1_000_000;
        public Action<long, int, TimeSpan> ProgressCallback { get; set; }
        public long ProgressInterval { get; set; } = 100_000;

        public HashFirstFilteredAttacker.Stats LastStats { get; private set; }

        public override Result AttemptRecover(Transcript t)
        {
            int L = t.VectorLength;
            if (L > 8)
            {
                throw new NotSupportedException(
                    "B.7 currently supports only VectorLength <= 8 (state packed into a long). " +
                    "For L > 8 swap the generator's long-keyed visited set for byte[]-keyed.");
            }

            // 1) Reconstruct Stat from observed R sequence (same logic as B.3).
            var stat = new Stat(L, "reconstructed");
            foreach (var round in t.Iterate)
            {
                if (round.R != null) stat.UpdateWithRandomVector(round.R);
            }

            // 2) Build per-position probability matrix.
            //
            //    StatItem.Probabilities[v] is a cumulative weight that sums to 256
            //    across v (initial all-1, redistributed by drift+reset under
            //    UpdateWithNumber; total mass conserved). Divide by 256 to get a
            //    proper probability distribution per position.
            //
            //    Zero entries (where the cumulative weight ended at 0) are
            //    *forbidden* values that the generator will skip entirely — no
            //    log-of-zero hazard, no magic floor.
            double[,] probs = new double[L, 256];
            for (int i = 0; i < L; i++)
            {
                double[] w = stat.StatItems[i].Probabilities;
                for (int v = 0; v < 256; v++)
                {
                    probs[i, v] = w[v] / 256.0;
                }
            }

            // 3) Stream candidate vectors from the enumerator into the hash-matching
            //    pipeline. We use an Action<byte[]> sampler because that's what
            //    HashFirstFilteredAttacker exposes; under the hood we pull from
            //    the generator's IEnumerable on each invocation.
            using var enumerator = ProbabilisticVectorGenerator.Enumerate(probs, L, Mode_).GetEnumerator();
            long enumYielded = 0;
            long exhaustedFillerCount = 0;
            Action<byte[]> sampler = (buf) =>
            {
                if (enumerator.MoveNext())
                {
                    var v = enumerator.Current.Vector;
                    Buffer.BlockCopy(v, 0, buf, 0, L);
                    enumYielded++;
                }
                else
                {
                    // Exhausted: lattice fully visited within (L positions × non-zero
                    // ranks). For Tail mode at break_demo (h_AB=3, p_upd=1.0) this
                    // happens at ~1M samples because Stat sharpens to ~6 non-zero
                    // bytes per position over ~100 iter rounds (6^8 ≈ 1.7M).
                    //
                    // Filler must be incrementally distinct from prior fillers,
                    // otherwise SHA(filler) is constant and accumulates spurious
                    // cascaded-hit counts on whatever round happens to match. Use
                    // a counter as a uniform fallback so post-exhaustion samples
                    // contribute as random rather than as one fixed vector.
                    exhaustedFillerCount++;
                    for (int i = 0; i < L; i++)
                    {
                        buf[i] = (byte)((exhaustedFillerCount >> (i * 8)) & 0xFF);
                    }
                }
            };

            // 4) Delegate to HashFirstFilteredAttacker pipeline (same as B.3).
            var inner = new HashFirstFilteredAttacker
            {
                PoolSize = PoolSize,
                SampleInto = sampler,
                ProgressCallback = ProgressCallback,
                ProgressInterval = ProgressInterval
            };
            var res = inner.AttemptRecover(t);
            LastStats = inner.LastStats;
            res.Notes = $"mode={Mode_}, enum_yielded={enumYielded}, exhausted_fillers={exhaustedFillerCount}, {res.Notes}";
            return res;
        }
    }

    /// <summary>
    /// Streams byte vectors in strictly decreasing order of joint probability under
    /// a per-position independent distribution. Lawler-style k-best enumeration
    /// over a product lattice with priority queue + visited set.
    ///
    /// Forbidden values (probability == 0) are excluded from each position's rank
    /// list, so they are *unreachable* — no log-of-zero floor needed.
    ///
    /// Cost is updated incrementally per neighbour (O(1) instead of O(L)).
    /// Visited set is updated *before* enqueue to avoid pushing duplicates onto
    /// the heap (smaller queue, faster heap ops).
    ///
    /// Algorithm structure adapted from a clean reference implementation kindly
    /// supplied by the user; see commit message for F_hab6 follow-up.
    /// </summary>
    public static class ProbabilisticVectorGenerator
    {
        /// <summary>
        /// Enumeration direction.
        /// <para><b>Typical</b>: vectors yielded in DECREASING joint probability
        /// (most-probable joint vector first; bytes at their locally-most-frequent
        /// values). Default for general-purpose Stat-prior search.</para>
        /// <para><b>Tail</b>: vectors yielded in INCREASING joint probability
        /// (least-probable joint vector first; bytes at their locally-rarest
        /// values). Right direction against protocols that filter password
        /// vectors against Stat-typicality (e.g. CandMax filter in ClientB).</para>
        /// </summary>
        public enum Mode
        {
            Typical,
            Tail
        }

        public readonly record struct Result(
            byte[] Vector,
            double Probability,
            double NegativeLogProbability
        );

        private readonly record struct ValueInfo(
            byte Value,
            double Probability,
            double Cost
        );

        private readonly record struct Node(
            ulong Key,
            double Cost
        );

        /// <summary>
        /// Enumerate vectors of length <paramref name="vectorLength"/> in decreasing
        /// order of joint probability under <paramref name="probabilities"/>.
        /// </summary>
        /// <param name="probabilities">
        ///   probabilities[i, v] = probability that byte at position i takes value v.
        ///   Must satisfy 0 &lt;= p &lt;= 1, NaN/Infinity rejected. Sums per position
        ///   need not equal 1 (relative ordering only matters for enumeration; the
        ///   reported Probability is the raw product). Zero entries are treated as
        ///   forbidden and excluded.
        /// </param>
        /// <param name="vectorLength">
        ///   Number of byte positions. Must equal probabilities.GetLength(0). Must
        ///   be &lt;= 8 (state is packed into a 64-bit ulong for visited-set keying).
        /// </param>
        public static IEnumerable<Result> Enumerate(
            double[,] probabilities,
            int vectorLength,
            Mode mode = Mode.Typical)
        {
            ValidateProbabilities(probabilities, vectorLength);

            ValueInfo[][] rankedValues = BuildRankedValues(probabilities, vectorLength, mode);

            // If any position has zero allowed values the joint distribution is empty.
            for (int position = 0; position < vectorLength; position++)
            {
                if (rankedValues[position].Length == 0) yield break;
            }

            // Cost = -log(p) (positive; smaller = more probable). For Typical
            // mode we enumerate in increasing-cost order (most-probable first):
            // priority = cost. For Tail mode we enumerate in decreasing-cost
            // order (least-probable first): priority = -cost so the min-heap
            // pops the largest cost first.
            var queue = new PriorityQueue<Node, double>();
            var visited = new HashSet<ulong>();

            ulong startKey = 0UL;
            double startCost = 0.0;
            for (int position = 0; position < vectorLength; position++)
            {
                startCost += rankedValues[position][0].Cost;
            }
            queue.Enqueue(new Node(startKey, startCost), Priority(startCost, mode));
            visited.Add(startKey);

            while (queue.Count > 0)
            {
                Node current = queue.Dequeue();

                byte[] vector = new byte[vectorLength];
                double probability = 1.0;
                for (int position = 0; position < vectorLength; position++)
                {
                    int rank = GetRank(current.Key, position);
                    ValueInfo info = rankedValues[position][rank];
                    vector[position] = info.Value;
                    probability *= info.Probability;
                }

                yield return new Result(
                    Vector: vector,
                    Probability: probability,
                    NegativeLogProbability: current.Cost
                );

                // Generate L neighbours: bump rank+1 at each position. Incremental
                // cost = current.Cost - oldCost + newCost (O(1) per neighbour).
                for (int position = 0; position < vectorLength; position++)
                {
                    int oldRank = GetRank(current.Key, position);
                    int newRank = oldRank + 1;
                    if (newRank >= rankedValues[position].Length) continue;

                    ulong nextKey = SetRank(current.Key, position, newRank);
                    if (!visited.Add(nextKey)) continue;

                    double nextCost =
                        current.Cost
                        - rankedValues[position][oldRank].Cost
                        + rankedValues[position][newRank].Cost;
                    queue.Enqueue(new Node(nextKey, nextCost), Priority(nextCost, mode));
                }
            }
        }

        private static double Priority(double cost, Mode mode)
            => mode == Mode.Typical ? cost : -cost;

        private static ValueInfo[][] BuildRankedValues(double[,] probabilities, int vectorLength, Mode mode)
        {
            var rankedValues = new ValueInfo[vectorLength][];
            for (int position = 0; position < vectorLength; position++)
            {
                var nonZero = Enumerable.Range(0, 256)
                    .Where(value => probabilities[position, value] > 0.0)
                    .Select(value => new ValueInfo(
                        Value: (byte)value,
                        Probability: probabilities[position, value],
                        Cost: -Math.Log(probabilities[position, value])
                    ));

                // rank 0 = "best for the chosen mode" — most probable for Typical,
                // least probable for Tail. ThenBy(x.Value) gives a stable tiebreak
                // (matters when many bytes share the same probability, e.g. fresh
                // Stat at run start where every value has weight 1).
                rankedValues[position] = (mode == Mode.Typical
                        ? nonZero.OrderByDescending(x => x.Probability).ThenBy(x => x.Value)
                        : nonZero.OrderBy(x => x.Probability).ThenBy(x => x.Value))
                    .ToArray();
            }
            return rankedValues;
        }

        private static void ValidateProbabilities(double[,] probabilities, int vectorLength)
        {
            if (probabilities == null)
                throw new ArgumentNullException(nameof(probabilities));
            if (probabilities.GetLength(0) != vectorLength)
                throw new ArgumentException(
                    $"probabilities first dimension ({probabilities.GetLength(0)}) must equal vectorLength ({vectorLength}).");
            if (probabilities.GetLength(1) != 256)
                throw new ArgumentException(
                    $"probabilities second dimension must be 256 (got {probabilities.GetLength(1)}).");
            if (vectorLength > 8)
                throw new ArgumentException(
                    "vectorLength > 8 not supported (state is packed into a 64-bit ulong).");

            for (int position = 0; position < vectorLength; position++)
            {
                for (int value = 0; value < 256; value++)
                {
                    double p = probabilities[position, value];
                    if (double.IsNaN(p) || double.IsInfinity(p) || p < 0.0)
                        throw new ArgumentException(
                            $"probabilities[{position}, {value}] = {p} (must be a non-negative finite number).");
                }
            }
        }

        private static int GetRank(ulong key, int position)
        {
            int shift = position * 8;
            return (int)((key >> shift) & 0xFFUL);
        }

        private static ulong SetRank(ulong key, int position, int rank)
        {
            int shift = position * 8;
            ulong mask = 0xFFUL << shift;
            key &= ~mask;
            key |= ((ulong)rank & 0xFFUL) << shift;
            return key;
        }
    }
}
