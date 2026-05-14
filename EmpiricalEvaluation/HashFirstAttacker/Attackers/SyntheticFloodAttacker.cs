using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using GrataCascade.Core;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.6 — Synthetic Flood Attacker (F5.e).
    ///
    /// Direct empirical validation of the F5 flooding-defense core claim:
    /// AES-Decrypt outputs on wrong passwords are indistinguishable from the real
    /// seed, across the K^M combination space an attacker would face after
    /// penetrating Stages A+B.
    ///
    /// Why synthetic: enumerating K^M combinations requires Stage-A+B survivors at
    /// density K per slot across all M slots, which requires attacker P
    /// ≥ 3.5×10¹³ (~350h generation + ~350h enumeration compute — infeasible).
    /// Since the indistinguishability claim is independent of HOW the candidates
    /// were obtained (only on AES's behaviour under wrong passwords), this
    /// attacker constructs the post-filter state synthetically: 1 real vector +
    /// (K-1) uniform-random fakes per slot. Each of the K^M enumerated
    /// combinations then represents one of the post-filter outcomes the real
    /// flood-scale attacker would see.
    ///
    /// Per round: enumerate 2^M combinations (K=2), AES-Decrypt each, apply
    /// distinguishability metrics. Measure rank of the real-seed's metric values
    /// among the 2^M outputs. Under H₀ (indistinguishable) rank is uniform in
    /// [1, 2^M]; under H₁ (detectable leak) rank clusters at extremes.
    ///
    /// K=4 is optional (4^16 ≈ 4.3×10⁹ per round, infeasible with PBKDF2 pre-
    /// round of AES key derivation). Default: K=2.
    /// </summary>
    public sealed class SyntheticFloodAttacker : AttackerBase
    {
        public override string Name => "B.6 Synthetic Flood (F5.e)";

        /// <summary>Candidates per slot. Real + (K-1) random fakes. K=2 gives 2^M enumeration.</summary>
        public int K { get; set; } = 2;

        /// <summary>
        /// Max rounds to process from a transcript. 0 = all iterate rounds with ground truth.
        /// Use for quick-iterate smoke tests.
        /// </summary>
        public int MaxRoundsPerTranscript { get; set; } = 0;

        /// <summary>
        /// Emit per-round CSV lines via this callback (headers: round,combination_count,real_rank_entropy,real_rank_chi2,real_rank_compression,real_rank_deviation).
        /// Null = no per-round emission.
        /// </summary>
        public Action<string> PerRoundCsvCallback { get; set; }

        public sealed class RoundDiag
        {
            public int RoundIndex;
            public int M;                    // number of password vectors this round
            public long CombinationCount;    // K^M
            public double RealEntropy;
            public double RealChi2;
            public double RealCompression;
            public double RealDeviation;
            public long RealRankEntropy;     // 1-based; position of real's metric value in sorted list (ties broken by index)
            public long RealRankChi2;
            public long RealRankCompression;
            public long RealRankDeviation;
            // Control sample: independently-drawn SecureRandom 32 bytes (not the real seed).
            // Used to separate "SecureRandom-vs-AES" artifact from "real-seed-specific" signal.
            public double CtrlEntropy;
            public double CtrlChi2;
            public double CtrlCompression;
            public double CtrlDeviation;
            public long CtrlRankEntropy;
            public long CtrlRankChi2;
            public long CtrlRankCompression;
            public long CtrlRankDeviation;
            public double MeanEntropy;
            public double MeanChi2;
            public double MeanCompression;
            public double MeanDeviation;
        }

        public sealed class Diagnostic
        {
            public List<RoundDiag> Rounds = new List<RoundDiag>();
            public int RoundsProcessed;
            public int RoundsSkipped;       // missing ground truth
            public long TotalDecryptions;
        }

        public Diagnostic LastDiag { get; private set; }

        public override Result AttemptRecover(Transcript t)
        {
            var diag = new Diagnostic();
            var sw = Stopwatch.StartNew();
            var rng = new Random();

            int limit = MaxRoundsPerTranscript > 0 ? Math.Min(MaxRoundsPerTranscript, t.Iterate.Count) : t.Iterate.Count;
            for (int rIdx = 0; rIdx < limit; rIdx++)
            {
                var rr = t.Iterate[rIdx];
                if (rr.GroundTruthPasswordVectors == null || rr.GroundTruthSeedB == null || rr.AesCiphertext == null)
                {
                    diag.RoundsSkipped++;
                    continue;
                }

                var result = ProcessRound(rIdx, rr, rng);
                diag.Rounds.Add(result);
                diag.RoundsProcessed++;
                diag.TotalDecryptions += result.CombinationCount;

                PerRoundCsvCallback?.Invoke(FormatCsvLine(result));
            }

            sw.Stop();
            LastDiag = diag;
            string notes = $"K={K}, rounds_processed={diag.RoundsProcessed}, rounds_skipped={diag.RoundsSkipped}, total_decryptions={diag.TotalDecryptions}";

            return new Result
            {
                RecoveredK = null,
                Success = false,
                SlotsMatched = 0,
                CandidatesTried = diag.TotalDecryptions,
                Elapsed = sw.Elapsed,
                Notes = notes
            };
        }

        private RoundDiag ProcessRound(int rIdx, Transcript.IterateRound rr, Random rng)
        {
            int M = rr.GroundTruthPasswordVectors.Count;
            int vectorLen = rr.GroundTruthPasswordVectors[0].Length;
            if (M <= 0 || M > 62) // 2^62 enumerable ceiling; practically K=2 → M ≤ 62
                throw new InvalidOperationException($"Round {rIdx}: M={M} out of supported range");

            long combinationCount = 1;
            for (int i = 0; i < M; i++) combinationCount *= K;

            // Build per-slot candidate arrays. Index 0 = real, 1..K-1 = random fakes.
            byte[][][] slotCandidates = new byte[M][][];
            for (int i = 0; i < M; i++)
            {
                slotCandidates[i] = new byte[K][];
                slotCandidates[i][0] = rr.GroundTruthPasswordVectors[i];
                for (int k = 1; k < K; k++)
                {
                    var fake = new byte[vectorLen];
                    rng.NextBytes(fake);
                    slotCandidates[i][k] = fake;
                }
            }

            // Control sample: independently-drawn SecureRandom bytes, NOT the real seed.
            // Should be i.i.d. from same distribution as the real seed (both via SecureRandom).
            // If ctrl ranks show the same pattern as real ranks, the signal is a SecureRandom-
            // vs-AES-Decrypt distribution artifact (not a flood-defense leak).
            byte[] ctrlSample = new byte[rr.GroundTruthSeedB.Length];
            System.Security.Cryptography.RandomNumberGenerator.Fill(ctrlSample);
            double ctrlEntropy = ShannonEntropy(ctrlSample);
            double ctrlChi2 = ChiSquareUniform(ctrlSample);
            double ctrlCompression = CompressionRatio(ctrlSample);
            double ctrlDeviation = MaxDeviationFromCenter(ctrlSample);

            // Running metric stats across all combinations
            double sumEntropy = 0, sumChi2 = 0, sumCompression = 0, sumDeviation = 0;
            double realEntropy = 0, realChi2 = 0, realCompression = 0, realDeviation = 0;
            long rankEntropy = 0, rankChi2 = 0, rankCompression = 0, rankDeviation = 0;
            long ctrlRankEntropy = 0, ctrlRankChi2 = 0, ctrlRankCompression = 0, ctrlRankDeviation = 0;

            // Scratch buffers reused per combination
            byte[][] pickedVectors = new byte[M][];
            byte[] concat = new byte[M * vectorLen];

            // Decode combination index j into base-K digits d_0, d_1, ..., d_{M-1}
            // Enumerate all j ∈ [0, K^M) in sorted order so rank is deterministic.
            for (long j = 0; j < combinationCount; j++)
            {
                long jj = j;
                for (int i = 0; i < M; i++)
                {
                    int digit = (int)(jj % K);
                    pickedVectors[i] = slotCandidates[i][digit];
                    jj /= K;
                }

                // Sort pickedVectors lexicographically (protocol does the same pre-SHA256)
                // In-place sort: tiny array (M ≤ 32 typically), bubble-sort suffices
                for (int a = 0; a < M - 1; a++)
                {
                    for (int b = a + 1; b < M; b++)
                    {
                        if (CompareBytes(pickedVectors[a], pickedVectors[b]) > 0)
                        {
                            var tmp = pickedVectors[a];
                            pickedVectors[a] = pickedVectors[b];
                            pickedVectors[b] = tmp;
                        }
                    }
                }

                // Concatenate
                for (int i = 0; i < M; i++)
                    Buffer.BlockCopy(pickedVectors[i], 0, concat, i * vectorLen, vectorLen);

                // password = SHA256(concat)
                byte[] password = SHA256.HashData(concat);

                // decrypted = AES.Decrypt(ciphertext, password)
                byte[] decrypted;
                try
                {
                    decrypted = AES.Decrypt(rr.AesCiphertext, password, null);
                }
                catch
                {
                    continue; // skip malformed
                }

                double ent = ShannonEntropy(decrypted);
                double chi = ChiSquareUniform(decrypted);
                double cmp = CompressionRatio(decrypted);
                double dev = MaxDeviationFromCenter(decrypted);

                sumEntropy += ent;
                sumChi2 += chi;
                sumCompression += cmp;
                sumDeviation += dev;

                if (j == 0)
                {
                    realEntropy = ent;
                    realChi2 = chi;
                    realCompression = cmp;
                    realDeviation = dev;
                }
                else
                {
                    // Rank counter: how many non-real combinations have metric <= real's
                    if (ent < realEntropy) rankEntropy++;
                    if (chi < realChi2) rankChi2++;
                    if (cmp < realCompression) rankCompression++;
                    if (dev < realDeviation) rankDeviation++;
                }
                // Control rank: same rank computation for ctrl sample, independent of real
                if (ent < ctrlEntropy) ctrlRankEntropy++;
                if (chi < ctrlChi2) ctrlRankChi2++;
                if (cmp < ctrlCompression) ctrlRankCompression++;
                if (dev < ctrlDeviation) ctrlRankDeviation++;
            }

            // Rank is 1-based position of real in ascending sorted list of all K^M values
            // rank_real = 1 + (number of other values strictly less than real)
            long combs = combinationCount;

            return new RoundDiag
            {
                RoundIndex = rIdx,
                M = M,
                CombinationCount = combs,
                RealEntropy = realEntropy,
                RealChi2 = realChi2,
                RealCompression = realCompression,
                RealDeviation = realDeviation,
                RealRankEntropy = rankEntropy + 1,
                RealRankChi2 = rankChi2 + 1,
                RealRankCompression = rankCompression + 1,
                RealRankDeviation = rankDeviation + 1,
                CtrlEntropy = ctrlEntropy,
                CtrlChi2 = ctrlChi2,
                CtrlCompression = ctrlCompression,
                CtrlDeviation = ctrlDeviation,
                CtrlRankEntropy = ctrlRankEntropy + 1,
                CtrlRankChi2 = ctrlRankChi2 + 1,
                CtrlRankCompression = ctrlRankCompression + 1,
                CtrlRankDeviation = ctrlRankDeviation + 1,
                MeanEntropy = sumEntropy / combs,
                MeanChi2 = sumChi2 / combs,
                MeanCompression = sumCompression / combs,
                MeanDeviation = sumDeviation / combs
            };
        }

        // ----- Distinguishability metrics -----

        private static double ShannonEntropy(byte[] data)
        {
            int[] counts = new int[256];
            foreach (var b in data) counts[b]++;
            double n = data.Length;
            double h = 0;
            for (int i = 0; i < 256; i++)
            {
                if (counts[i] == 0) continue;
                double p = counts[i] / n;
                h -= p * Math.Log2(p);
            }
            return h;
        }

        /// <summary>
        /// Chi-square statistic against uniform distribution over 256 bins.
        /// For 32-byte input, expected per bin = 32/256 = 0.125. Low-sample regime,
        /// but useful as a relative-ranking metric across many decryptions.
        /// </summary>
        private static double ChiSquareUniform(byte[] data)
        {
            int[] counts = new int[256];
            foreach (var b in data) counts[b]++;
            double expected = data.Length / 256.0;
            double chi2 = 0;
            for (int i = 0; i < 256; i++)
            {
                double diff = counts[i] - expected;
                chi2 += diff * diff / expected;
            }
            return chi2;
        }

        private static double CompressionRatio(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                ds.Write(data, 0, data.Length);
            }
            return ms.Length / (double)data.Length;
        }

        /// <summary>Max |byte - 128|. Random expected ~sqrt(var of uniform) + centered.</summary>
        private static double MaxDeviationFromCenter(byte[] data)
        {
            double max = 0;
            foreach (var b in data)
            {
                double d = Math.Abs(b - 128.0);
                if (d > max) max = d;
            }
            return max;
        }

        private static int CompareBytes(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] < b[i]) return -1;
                if (a[i] > b[i]) return 1;
            }
            return a.Length.CompareTo(b.Length);
        }

        private static string FormatCsvLine(RoundDiag r)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join(",", new string[] {
                r.RoundIndex.ToString(ic),
                r.M.ToString(ic),
                r.CombinationCount.ToString(ic),
                r.RealEntropy.ToString("G17", ic),
                r.RealChi2.ToString("G17", ic),
                r.RealCompression.ToString("G17", ic),
                r.RealDeviation.ToString("G17", ic),
                r.RealRankEntropy.ToString(ic),
                r.RealRankChi2.ToString(ic),
                r.RealRankCompression.ToString(ic),
                r.RealRankDeviation.ToString(ic),
                r.CtrlEntropy.ToString("G17", ic),
                r.CtrlChi2.ToString("G17", ic),
                r.CtrlCompression.ToString("G17", ic),
                r.CtrlDeviation.ToString("G17", ic),
                r.CtrlRankEntropy.ToString(ic),
                r.CtrlRankChi2.ToString(ic),
                r.CtrlRankCompression.ToString(ic),
                r.CtrlRankDeviation.ToString(ic),
                r.MeanEntropy.ToString("G17", ic),
                r.MeanChi2.ToString("G17", ic),
                r.MeanCompression.ToString("G17", ic),
                r.MeanDeviation.ToString("G17", ic)
            });
        }

        public static string CsvHeader => "round,M,combinations,real_entropy,real_chi2,real_compression,real_deviation,real_rank_entropy,real_rank_chi2,real_rank_compression,real_rank_deviation,ctrl_entropy,ctrl_chi2,ctrl_compression,ctrl_deviation,ctrl_rank_entropy,ctrl_rank_chi2,ctrl_rank_compression,ctrl_rank_deviation,mean_entropy,mean_chi2,mean_compression,mean_deviation";
    }
}
