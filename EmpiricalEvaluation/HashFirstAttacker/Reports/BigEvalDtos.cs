using System;

namespace HashFirstAttacker.Reports
{
    /// <summary>
    /// Per-attacker aggregate statistics produced by <see cref="BigEval.Run"/>
    /// and consumed by <see cref="BigEvalReport.WriteMarkdown"/>.
    /// </summary>
    public sealed class BigEvalPerAttacker
    {
        public string Name;
        public int Runs;
        public int Successes;
        public int TotalSlotsMatched;
        public int TotalSlotsPossible;
        public long TotalCandidates;
        public double TotalSeconds;
        public string ExtraNotes;

        public double SuccessRate => Runs > 0 ? (double)Successes / Runs : 0;
        public double AvgSeconds => Runs > 0 ? TotalSeconds / Runs : 0;
        public double AvgCandidates => Runs > 0 ? (double)TotalCandidates / Runs : 0;
        public double AvgSlotsMatched => Runs > 0 ? (double)TotalSlotsMatched / Runs : 0;
        public double UpperCI95 => Runs > 0 ? ClopperPearson.UpperCI(Successes, Runs, 0.05) : 0;
    }

    /// <summary>
    /// Run-level configuration recorded in the BigEvalReport header.
    /// </summary>
    public sealed class BigEvalConfig
    {
        public string UtcStart;
        public string UtcEnd;
        public int Runs;
        public TimeSpan B1Budget;
        public long B2B3Pool;
        public int B4DiscSamples;
        public int Workers;
        public string TranscriptFileSha;
        public double SingleThreadSha256OpsPerSec;
        public double ShannonEntropyB1;
        public long TotalShaEvaluations;
        public TimeSpan Elapsed;
        public int VectorLength, VectorCount, HashLenAtoB, HashLenBtoA, HashLenPassword;
    }

    /// <summary>
    /// Aggregated B.4 oracle discriminator metrics across all transcripts.
    /// </summary>
    public sealed class B4OracleAggregate
    {
        public int RoundsMeasured;
        public long TPSum;
        public long FPSum;
        public double MeanTP;
        public double MeanFP;
        public int MaxTP;
        public int MaxFP;
        public double VarTP;
        public double VarFP;
        public double ExpectedFPPerRound;
        public double CohensD;
        public long TotalDiscriminatorQueries;
    }

    /// <summary>
    /// B.2 cascaded-filter per-round survival breakdown across all transcripts.
    /// </summary>
    public sealed class B2PerRoundBreakdown
    {
        public int RoundsWithStageAHits;
        public int RoundsWithStageBHits;
        public int RoundsWithStageCHits;
        public long TotalStageAHits;
        public long TotalStageBHits;
        public long TotalStageCHits;
    }
}
