using System;
using System.Collections.Generic;
using System.IO;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// Runs one protocol round exactly like Form1.button2_Click did, while recording
    /// the public transcript. Does NOT modify protocol source — it only reads the
    /// already-public fields of messages exchanged between ClientA and ClientB.
    /// </summary>
    public sealed class ProtocolRunner
    {
        // Reference v2 default: read from Configuration (set by ConfigLoader.Load via
        // the legacy bridge). Reference v2 baseline = 500. CLI commands can override
        // by setting MaxIterations explicitly via --max-iter.
        public int MaxIterations { get; set; } = Configuration.MaxIterations;
        public bool SuppressProtocolStdout { get; set; } = true;

        public sealed class Outcome
        {
            // Core run result.
            public bool FinalHit;
            public bool KeysMatch;
            public int Iterations;
            public byte[] KeyA;
            public byte[] KeyB;
            public Transcript Transcript;

            // F4 calibration diagnostics (CandMax filter + CutLimit pool refill).
            public F4Metrics F4 = new F4Metrics();

            // F10 p_upd diagnostics (Stat concentration on ClientA's stats[0]).
            public F10Metrics F10 = new F10Metrics();

            // ---- Backwards-compat shims so existing readers keep compiling ----
            // (R4 refactor: forwarded to nested F4/F10 metric containers.)
            public double AvgPasswordProbLog2 { get => F4.AvgPasswordProbLog2; set => F4.AvgPasswordProbLog2 = value; }
            public int RoundsSkippedByCandidateFilter { get => F4.RoundsSkippedByCandidateFilter; set => F4.RoundsSkippedByCandidateFilter = value; }
            public int FillRejections { get => F4.FillRejections; set => F4.FillRejections = value; }
            public int FillSafetyTriggers { get => F4.FillSafetyTriggers; set => F4.FillSafetyTriggers = value; }
            public double StatEntropyAvg { get => F10.StatEntropyAvg; set => F10.StatEntropyAvg = value; }
            public double StatMaxProbAvg { get => F10.StatMaxProbAvg; set => F10.StatMaxProbAvg = value; }
        }

        /// <summary>
        /// F4 calibration diagnostics: outcome of the CandidateMax filter and
        /// CutLimit pool-refill safeties for a single protocol run.
        /// </summary>
        public sealed class F4Metrics
        {
            /// <summary>
            /// Mean Stat-log2-probability of the password vectors chosen by
            /// ClientA (identical to ClientB's by construction post-Final).
            /// Negative; more negative = rarer = stronger filter effect. NaN if
            /// the run did not reach Final.
            /// </summary>
            public double AvgPasswordProbLog2 = double.NaN;

            /// <summary>
            /// Count of rounds where ClientB skipped password-candidate selection
            /// because the post-filter passedVectors count fell below
            /// AESPassedVectorsCount. Populated only when at least one of the F4
            /// thresholds is active.
            /// </summary>
            public int RoundsSkippedByCandidateFilter;

            /// <summary>
            /// CutLimit metric: aggregated A+B pool-vector rejections across all
            /// FillVectors calls this run. Higher = filter is actively rejecting
            /// candidates during pool refill.
            /// </summary>
            public int FillRejections;

            /// <summary>
            /// CutLimit metric: aggregated A+B count of safety-cap triggers
            /// (vectors accepted unfiltered after 1000 failed attempts). Non-zero
            /// = CutLimit threshold is too strict for the pool's log-prob
            /// distribution.
            /// </summary>
            public int FillSafetyTriggers;
        }

        /// <summary>
        /// F10 p_upd diagnostics: Stat-distribution concentration measured on
        /// ClientA's oldest Stat (the one driving the most recent pool refill).
        /// </summary>
        public sealed class F10Metrics
        {
            /// <summary>
            /// Mean Shannon entropy across ClientA's stats[0] byte positions.
            /// Uniform = log2(256) = 8; fully concentrated = 0. Lower = stronger
            /// Stat concentration. Populated for both converged and
            /// non-converged runs.
            /// </summary>
            public double StatEntropyAvg = double.NaN;

            /// <summary>
            /// Mean of max-probability per byte position across ClientA's
            /// stats[0]. Uniform = 1/256 ≈ 0.0039; concentrated = approaches 1.
            /// </summary>
            public double StatMaxProbAvg = double.NaN;
        }

        public Outcome Run()
        {
            var transcript = new Transcript
            {
                VectorLength = Configuration.VectorLength,
                VectorCount = Configuration.VectorCount,
                HashLenAtoB = Configuration.HASHLength_AtoB,
                HashLenBtoA = Configuration.HASHLength_BtoA,
                HashLenPassword = Configuration.HASHLength_Password,
                PasswordSlots = Configuration.AESPassedVectorsCount
            };

            var originalOut = System.Console.Out;
            if (SuppressProtocolStdout) System.Console.SetOut(TextWriter.Null);

            var outcome = new Outcome { Transcript = transcript };
            try
            {
                var clientA = new ClientA();
                var clientB = new ClientB();

                int i = 1;
                for (; i <= MaxIterations; i++)
                {
                    MessageToClientB msgToB = clientA.CreateMessageToB();
                    MessageToClientA msgToA = clientB.ProcessMessageFromA(msgToB, i);

                    if (msgToA.Tag != null && msgToA.Tag.Contains("Final"))
                    {
                        transcript.Final = new Transcript.FinalRound
                        {
                            Tag = i,
                            AtoB_Hashes = new List<HASH>(msgToB.V),
                            BtoA_PasswordHashes = new List<HASH>(msgToA.hashes)
                        };
                        clientA.ProcessMessageFromB(msgToA, i);
                        outcome.FinalHit = true;
                        break;
                    }

                    // Capture ClientB's seed AFTER ProcessMessageFromA (it was set during that call
                    // when passedVectors.Count > 0). This is public state, used here only for
                    // oracle TP/FP ground-truth — never fed to the attacker during real attacks.
                    byte[] seedSnapshot = null;
                    if (clientB.seed != null)
                    {
                        seedSnapshot = new byte[clientB.seed.Length];
                        Buffer.BlockCopy(clientB.seed, 0, seedSnapshot, 0, clientB.seed.Length);
                    }

                    // F5.e/B.6 ground-truth: snapshot the M password vectors ClientB used
                    // for password = SHA256(sorted concat). Already cloned inside ClientB,
                    // safe to reference directly.
                    List<byte[]> pwVectorsSnapshot = clientB.LastPassedVectorsSnapshot;

                    transcript.Iterate.Add(new Transcript.IterateRound
                    {
                        Tag = i,
                        AtoB_Hashes = new List<HASH>(msgToB.V),
                        R = msgToA.R,
                        BtoA_Hashes = new List<HASH>(msgToA.hashes),
                        AesCiphertext = msgToA.message,
                        GroundTruthSeedB = seedSnapshot,
                        GroundTruthPasswordVectors = pwVectorsSnapshot
                    });

                    clientA.ProcessMessageFromB(msgToA, i);
                }
                outcome.Iterations = i;
                outcome.RoundsSkippedByCandidateFilter = clientB.RoundsSkippedByF4Filter;
                outcome.FillRejections = clientA.FillRejections + clientB.FillRejections;
                outcome.FillSafetyTriggers = clientA.FillSafetyTriggers + clientB.FillSafetyTriggers;

                if (outcome.FinalHit)
                {
                    outcome.KeyA = clientA.GetPassword();
                    outcome.KeyB = clientB.GetPassword();
                    outcome.KeysMatch = ByteEq(outcome.KeyA, outcome.KeyB);
                    transcript.GroundTruthK = outcome.KeyA;

                    // F4 metric: avg log2-probability of password vectors
                    if (clientA.passwordVectors != null && clientA.passwordVectors.Count > 0)
                    {
                        double sum = 0;
                        foreach (var v in clientA.passwordVectors) sum += v.Probability;
                        outcome.AvgPasswordProbLog2 = sum / clientA.passwordVectors.Count;
                    }
                }

                // F10 metric: Stat distribution concentration from ClientA's most
                // recent Stat (the one driving the last pool refill). Populated for
                // both converged and non-converged runs — for non-converged runs
                // this captures the state at the MaxIterations cap, which is the
                // diagnostic signal for why the run stalled.
                ComputeStatStats(clientA.stats, outcome);
            }
            finally
            {
                if (SuppressProtocolStdout) System.Console.SetOut(originalOut);
            }
            return outcome;
        }

        private static bool ByteEq(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void ComputeStatStats(System.Collections.Generic.List<GrataCascade.Core.Stat> stats, Outcome outcome)
        {
            if (stats == null || stats.Count == 0) return;
            // Use stats[0] — the oldest Stat in the list, which has been updated
            // once per Iterate round since run start (N-1 updates by the end of
            // an N-iteration run). stats[-1] is the Stat just added in the most
            // recent FillVectors call and has only 0 or 1 updates, so measuring
            // it would tautologically show uniform entropy.
            var last = stats[0];
            if (last?.StatItems == null || last.StatItems.Length == 0) return;

            double totalEntropy = 0;
            double totalMaxProb = 0;
            int count = 0;
            foreach (var item in last.StatItems)
            {
                if (item?.Probabilities == null) continue;
                double sum = 0;
                for (int k = 0; k < item.Probabilities.Length; k++) sum += item.Probabilities[k];
                if (sum <= 0) continue;

                double entropy = 0;
                double maxProb = 0;
                for (int k = 0; k < item.Probabilities.Length; k++)
                {
                    double p = item.Probabilities[k] / sum;
                    if (p > 0) entropy -= p * System.Math.Log2(p);
                    if (p > maxProb) maxProb = p;
                }
                totalEntropy += entropy;
                totalMaxProb += maxProb;
                count++;
            }
            if (count > 0)
            {
                outcome.StatEntropyAvg = totalEntropy / count;
                outcome.StatMaxProbAvg = totalMaxProb / count;
            }
        }
    }
}
