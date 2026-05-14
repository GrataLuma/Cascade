using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.2 — Hash-First Filtered Attacker.
    /// Streams P candidate vectors and pushes each through the three-stage filter
    /// pipeline described in the task:
    ///
    ///   a) SHA256(v)[0..HashLenAtoB)           matches any round's A→B hash
    ///   b) SHA256(v)[HashLenAtoB..+HashLenBtoA) matches that round's B→A hashes
    ///   c) SHA256(v)[offsetPW..+HashLenPassword) matches the Final password hash
    ///
    /// Memory layout: no large pool is stored — vectors are generated and consumed
    /// on the fly. Only the transcript-derived hash indices live in memory.
    /// </summary>
    public sealed class HashFirstFilteredAttacker : AttackerBase
    {
        public override string Name => "B.2 Hash-First Filtered";

        public long PoolSize { get; set; } = 10_000_000;
        public TimeSpan Budget { get; set; } = TimeSpan.MaxValue;
        public Action<long, int, TimeSpan> ProgressCallback { get; set; }
        public long ProgressInterval { get; set; } = 500_000;

        /// <summary>
        /// Optional pluggable sampler. If set, called to fill each candidate vector;
        /// otherwise a CSPRNG uniform fill is used (B.2 default behaviour).
        /// Used by B.3 to inject Stat-driven structured sampling.
        /// </summary>
        public Action<byte[]> SampleInto { get; set; }

        /// <summary>Diagnostics populated during the attack.</summary>
        public sealed class Stats
        {
            public int[] AtoBSurvivorsPerRound;
            public int[] BtoASurvivorsPerRound;
            public int[] FinalCandidatesPerSlot;
            public long TotalAtoBHits;
            public long TotalBtoAHits;
            public long TotalFinalHits;
        }

        public Stats LastStats { get; private set; }

        public override Result AttemptRecover(Transcript t)
        {
            int hA = t.HashLenAtoB;
            int hB = t.HashLenBtoA;
            int hP = t.HashLenPassword;
            int offsetB = hA;
            int offsetP = hA + hB;
            int vectorLen = t.VectorLength;
            int rounds = t.Iterate.Count;
            int slotCount = t.Final.BtoA_PasswordHashes.Count;

            // Build indices from the transcript.
            var atoBToRounds = new Dictionary<ulong, List<int>>(rounds * t.VectorCount);
            var btoASets = new HashSet<ulong>[rounds];
            for (int r = 0; r < rounds; r++)
            {
                var round = t.Iterate[r];
                foreach (var h in round.AtoB_Hashes)
                {
                    ulong k = ReadUlong(h.Data, 0, hA);
                    if (!atoBToRounds.TryGetValue(k, out var list))
                    {
                        list = new List<int>();
                        atoBToRounds[k] = list;
                    }
                    list.Add(r);
                }
                var set = new HashSet<ulong>();
                foreach (var h in round.BtoA_Hashes) set.Add(ReadUlong(h.Data, 0, hB));
                btoASets[r] = set;
            }
            var finalBySlot = new Dictionary<ulong, int>(slotCount);
            for (int s = 0; s < slotCount; s++)
            {
                ulong k = ReadUlong(t.Final.BtoA_PasswordHashes[s].Data, 0, hP);
                if (!finalBySlot.ContainsKey(k)) finalBySlot[k] = s;
            }

            var stats = new Stats
            {
                AtoBSurvivorsPerRound = new int[rounds],
                BtoASurvivorsPerRound = new int[rounds],
                FinalCandidatesPerSlot = new int[slotCount]
            };
            var candidatesPerSlot = new byte[slotCount][];
            int slotsFilled = 0;

            var rng = RandomNumberGenerator.Create();
            byte[] v = new byte[vectorLen];
            byte[] h32 = new byte[32];
            long samples = 0;
            var sw = Stopwatch.StartNew();
            long nextProgress = ProgressInterval;

            while (samples < PoolSize && sw.Elapsed < Budget)
            {
                if (SampleInto != null) SampleInto(v);
                else rng.GetBytes(v);
                SHA256.HashData(v, h32);
                ulong keyA = ReadUlong(h32, 0, hA);

                if (atoBToRounds.TryGetValue(keyA, out var rList))
                {
                    ulong keyB = ReadUlong(h32, offsetB, hB);
                    ulong keyP = ReadUlong(h32, offsetP, hP);
                    foreach (int r in rList)
                    {
                        stats.AtoBSurvivorsPerRound[r]++;
                        stats.TotalAtoBHits++;
                        if (btoASets[r].Contains(keyB))
                        {
                            stats.BtoASurvivorsPerRound[r]++;
                            stats.TotalBtoAHits++;
                            if (finalBySlot.TryGetValue(keyP, out int slot))
                            {
                                stats.FinalCandidatesPerSlot[slot]++;
                                stats.TotalFinalHits++;
                                if (candidatesPerSlot[slot] == null)
                                {
                                    candidatesPerSlot[slot] = (byte[])v.Clone();
                                    slotsFilled++;
                                }
                            }
                        }
                    }
                }

                samples++;
                if (samples >= nextProgress)
                {
                    ProgressCallback?.Invoke(samples, slotsFilled, sw.Elapsed);
                    nextProgress += ProgressInterval;
                }
            }
            sw.Stop();

            byte[] recoveredK = null;
            if (slotsFilled == slotCount)
            {
                var sorted = new List<byte[]>(candidatesPerSlot);
                sorted.Sort(LexCompare);
                var concat = new byte[sorted.Count * vectorLen];
                for (int i = 0; i < sorted.Count; i++) Buffer.BlockCopy(sorted[i], 0, concat, i * vectorLen, vectorLen);
                recoveredK = SHA256.HashData(concat);
            }

            double rate = sw.Elapsed.TotalSeconds > 0 ? samples / sw.Elapsed.TotalSeconds : 0;
            string notes = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "P={0}, rate={1:F0}/s, stage_a_hits={2}, stage_b_hits={3}, stage_c_hits={4}, slots={5}/{6}",
                samples, rate, stats.TotalAtoBHits, stats.TotalBtoAHits, stats.TotalFinalHits, slotsFilled, slotCount);

            LastStats = stats;

            return new Result
            {
                RecoveredK = recoveredK,
                Success = recoveredK != null && BytesEqual(recoveredK, t.GroundTruthK),
                SlotsMatched = slotsFilled,
                CandidatesTried = samples,
                Elapsed = sw.Elapsed,
                Notes = notes
            };
        }

        private static ulong ReadUlong(byte[] buf, int offset, int length)
        {
            ulong k = 0;
            for (int i = 0; i < length; i++) k = (k << 8) | buf[offset + i];
            return k;
        }

        private static int LexCompare(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] < b[i]) return -1;
                if (a[i] > b[i]) return 1;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
