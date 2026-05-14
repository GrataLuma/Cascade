using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    internal class Configuration
    {
        // Reference v2 (2026-05-02 pivot). See ProtocolConfiguration.BuildReference for
        // the canonical JSON; these statics are the legacy bridge that protocol code
        // still reads. Kept in sync via ConfigLoader.Load -> Configuration.LoadFrom.
        public static int VectorLength = 32;
        public static int VectorCount = 4096;
        public static int HASHLength_AtoB = 5;
        public static int HASHLength_BtoA = 2;
        public static int HASHLength_Password = 8;   // v2: was 4

        public static int SeedMinMax = 4;             // v2: was 3

        public static int AESPassedVectorsCount = 8;  // v2: was 16

        public static double NoUpdateLimit = 0.25;

        public static double TargetProbability = -512;

        // v2: hard cap on protocol iterations per run. Reference iter median ~70,
        // p99 ~115, max ~181 (F2 v1 baseline), so 500 leaves comfortable headroom.
        public static int MaxIterations = 500;

        // F4 (a): Threshold for removing over-typical vectors from the pool.
        // A newly generated vector is accepted only if stat.GetProbabilityLog(v)
        // < CutLimitProbabilityLog2 (i.e. its log2-probability is more negative
        // than the threshold). Vectors that fail are regenerated up to
        // CutLimitSafetyAttempts times before the safety cap accepts anything.
        // Set to double.NegativeInfinity to disable the filter.
        // Default: -8 (reject log2-prob >= -8).
        public static double CutLimitProbabilityLog2 = -8;

        // F4 (b): Maximum allowed probability for inclusion in password candidates.
        // In ClientB.ProcessMessageFromA, collision vectors with Probability
        // >= CandidateMaxProbabilityLog2 are excluded from the top-M candidate
        // selection. If the post-filter count falls below AESPassedVectorsCount,
        // the round is skipped (no password candidates this round).
        // Set to double.NegativeInfinity to disable the filter.
        //
        // Reference v2 (2026-05-02): default disabled (-8 = same threshold as cut limit,
        // i.e. effectively no filter). The candidate-max filter remains a configurable
        // structural defense (paper §4) — operators may tighten it (-95 was the F4 v1
        // calibrated optimum) when targeting Tier-3 deployment. Out of the box the
        // reference baseline runs without the filter for simplicity and to keep the
        // crack-point analysis (F8) in the always-attackable regime.
        public static double CandidateMaxProbabilityLog2 = -8;

        // F4: Safety counter for CutLimit pool regeneration. If the pool cannot
        // produce a passing vector within this many attempts, the last-generated
        // vector is accepted unfiltered and a per-round warning is emitted.
        public static int CutLimitSafetyAttempts = 1000;

        /// <summary>
        /// F9.b bridge: copy values from a <see cref="ProtocolConfiguration"/> instance
        /// into the legacy static fields. CLI modules call this once at startup after
        /// resolving --config. Not thread-safe across different configs in one process,
        /// which is fine for the current "one process = one config" deployment model
        /// (see F9.c alternative path in Zadani-followup.md).
        /// </summary>
        public static void LoadFrom(ProtocolConfiguration cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            VectorLength = cfg.VectorLength;
            VectorCount = cfg.VectorCount;
            HASHLength_AtoB = cfg.HashLengthAtoB;
            HASHLength_BtoA = cfg.HashLengthBtoA;
            HASHLength_Password = cfg.HashLengthPassword;
            SeedMinMax = cfg.SeedMinMax;
            AESPassedVectorsCount = cfg.AesPassedVectorsCount;
            NoUpdateLimit = cfg.NoUpdateLimit; // = 1 - UpdateProbability
            TargetProbability = cfg.TerminationThresholdLog2;
            CutLimitProbabilityLog2 = cfg.CutLimitProbabilityLog2;
            CandidateMaxProbabilityLog2 = cfg.CandidateMaxProbabilityLog2;
            CutLimitSafetyAttempts = cfg.CutLimitSafetyAttempts;
            MaxIterations = cfg.MaxIterations;
        }
    }
}
