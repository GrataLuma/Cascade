using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using GrataCascade.Core;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.4 — AES-Oracle Attacker.
    ///
    /// PRIMARY METRIC (per user feedback): discriminator power at the round level,
    /// not full K* recovery. For each round r where ClientB had a real seed:
    ///
    ///   TP: feed the REAL seed (ground truth) to the discriminator. Count hits
    ///       against round t+1 A→B hashes across K near-seed samples.
    ///   FP: feed a UNIFORM RANDOM byte[] seed. Count hits same way.
    ///
    /// The discriminator: sample K vectors with each byte = seed[i] + Next(-SeedMinMax..SeedMinMax)
    /// clamped to [0, 255]; for each, compute SHA256(v)[0..HashLenAtoB) and test
    /// membership in the next round's A→B hash set.
    ///
    /// Outputs: mean and max hits for TP population, same for FP, and a crude
    /// separation statistic. Full K* recovery attempts are intentionally NOT
    /// performed — the oracle's discriminative power is the question.
    ///
    /// NOTE: the GroundTruthSeedB field is populated by ProtocolRunner from
    /// ClientB.seed — public state, but only used here for test-harness ground
    /// truth; never fed into the "real" attacker flow (B.2, B.3).
    /// </summary>
    public sealed class AESOracleAttacker : AttackerBase
    {
        public override string Name => "B.4 AES-Oracle (discriminator power)";

        public int DiscriminatorSamples { get; set; } = 50_000;

        public sealed class RoundMeasurement
        {
            public int RoundIndex;
            public int TPHits;   // hits when discriminator seeded with real seed
            public int FPHits;   // hits when discriminator seeded with random bytes
        }

        public sealed class Diagnostic
        {
            public List<RoundMeasurement> Rounds = new List<RoundMeasurement>();
            public int RoundsMeasured;
            public double MeanTP;
            public double MeanFP;
            public int MaxTP;
            public int MaxFP;
            public int TPAbsoluteHitsSum;
            public int FPAbsoluteHitsSum;
        }
        public Diagnostic LastDiag { get; private set; }

        public override Result AttemptRecover(Transcript t)
        {
            var diag = new Diagnostic();
            var sw = Stopwatch.StartNew();
            var rng = new Random();

            // Iterate over round pairs (r, r+1). We need next round for the discriminator
            // and a real seed at round r (populated when ClientB had a match).
            for (int r = 0; r < t.Iterate.Count - 1; r++)
            {
                var roundR = t.Iterate[r];
                var roundR1 = t.Iterate[r + 1];
                if (roundR.GroundTruthSeedB == null) continue; // B had no match → no seed to test
                if (roundR1 == null || roundR1.AtoB_Hashes == null) continue;

                int tpHits = RunDiscriminator(roundR.GroundTruthSeedB, roundR1.AtoB_Hashes, t.HashLenAtoB, t.VectorLength, rng);

                // Sample a uniform random seed for FP. Use a NEW random seed per round so
                // we don't keep testing the same seed.
                byte[] randSeed = new byte[t.VectorLength];
                rng.NextBytes(randSeed);
                int fpHits = RunDiscriminator(randSeed, roundR1.AtoB_Hashes, t.HashLenAtoB, t.VectorLength, rng);

                diag.Rounds.Add(new RoundMeasurement { RoundIndex = r, TPHits = tpHits, FPHits = fpHits });
                diag.RoundsMeasured++;
                diag.TPAbsoluteHitsSum += tpHits;
                diag.FPAbsoluteHitsSum += fpHits;
                if (tpHits > diag.MaxTP) diag.MaxTP = tpHits;
                if (fpHits > diag.MaxFP) diag.MaxFP = fpHits;
            }

            sw.Stop();
            if (diag.RoundsMeasured > 0)
            {
                diag.MeanTP = (double)diag.TPAbsoluteHitsSum / diag.RoundsMeasured;
                diag.MeanFP = (double)diag.FPAbsoluteHitsSum / diag.RoundsMeasured;
            }

            LastDiag = diag;
            string notes = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "K={0}, rounds_measured={1}, mean_TP={2:F4}, mean_FP={3:F4}, max_TP={4}, max_FP={5}",
                DiscriminatorSamples, diag.RoundsMeasured, diag.MeanTP, diag.MeanFP, diag.MaxTP, diag.MaxFP);

            return new Result
            {
                RecoveredK = null,
                Success = false, // by design — we are not attempting K* recovery
                SlotsMatched = 0,
                CandidatesTried = (long)DiscriminatorSamples * 2 * diag.RoundsMeasured,
                Elapsed = sw.Elapsed,
                Notes = notes
            };
        }

        /// <summary>
        /// Sample K near-seed vectors (each byte = seed[i] + Next(-SeedMinMax..SeedMinMax) clamped).
        /// Count how many produce a prefix in the next round's A→B hash set.
        /// </summary>
        private int RunDiscriminator(byte[] seed, List<HASH> nextAtoBHashes, int hashLenA, int vectorLen, Random rng)
        {
            var hashSet = new HashSet<ulong>();
            foreach (var h in nextAtoBHashes) hashSet.Add(ReadUlong(h.Data, 0, hashLenA));

            int hits = 0;
            byte[] v = new byte[vectorLen];
            byte[] h32 = new byte[32];
            int range = Configuration.SeedMinMax * 2 + 1;
            int seedLen = Math.Min(seed.Length, vectorLen);
            for (int k = 0; k < DiscriminatorSamples; k++)
            {
                for (int i = 0; i < seedLen; i++)
                {
                    int d = rng.Next(range) - Configuration.SeedMinMax;
                    int nv = seed[i] + d;
                    if (nv < 0) nv = 0; else if (nv > 255) nv = 255;
                    v[i] = (byte)nv;
                }
                SHA256.HashData(v, h32);
                if (hashSet.Contains(ReadUlong(h32, 0, hashLenA))) hits++;
            }
            return hits;
        }

        private static ulong ReadUlong(byte[] buf, int offset, int length)
        {
            ulong k = 0;
            for (int i = 0; i < length; i++) k = (k << 8) | buf[offset + i];
            return k;
        }
    }
}
