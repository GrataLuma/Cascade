using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HashFirstAttacker.Cli;
using HashFirstAttacker.Reports;

namespace HashFirstAttacker
{
    /// <summary>
    /// Common Parallel.For boilerplate shared by every attacker phase in
    /// <see cref="BigEval"/>: alreadyDone skip, transcript load, per-attacker
    /// work via caller-supplied lambda, console progress + ETA, ignore
    /// OperationCanceledException raised by TerminateOnSuccess.
    ///
    /// Per-attacker stats aggregation, CSV row append, and progress.csv
    /// emission stay inside the caller's lambda — those differ enough across
    /// attackers (B.2 stage breakdown, B.4 oracle metrics, B.7 enum-yielded
    /// notes, etc.) that forcing them into a shared signature buys nothing.
    /// What this helper saves is the ~15 lines of identical Parallel.For
    /// + alreadyDone check + console-progress emission per attacker phase.
    /// </summary>
    public static class BigEvalAttackerRunner
    {
        /// <summary>
        /// Run one attacker phase across all transcripts in
        /// <paramref name="reader"/>. The caller-supplied
        /// <paramref name="runOne"/> lambda is invoked once per transcript
        /// that wasn't already done (and not in a cancelled state); it owns
        /// attacker construction, AttemptRecover invocation, stats lock-update,
        /// CSV row append, and TerminateOnSuccess.
        /// </summary>
        public static void RunPhase(
            string slotName,
            string consoleTag,
            TranscriptReader reader,
            ParallelOptions po,
            System.Collections.Generic.HashSet<(string, int)> alreadyDone,
            BigEvalPerAttacker stats,
            Action<int /*transcriptIdx*/, Transcript /*transcript*/, int /*slots*/> runOne)
        {
            int done = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                Parallel.For(0, reader.Count, po, i =>
                {
                    if (alreadyDone.Contains((slotName, i)))
                    {
                        Interlocked.Increment(ref done);
                        return;
                    }
                    var t = reader.Load(i);
                    int slots = t.Final.BtoA_PasswordHashes.Count;

                    runOne(i, t, slots);

                    int d = Interlocked.Increment(ref done);
                    if (d % 30 == 0 || d == reader.Count)
                    {
                        double pct = 100.0 * d / reader.Count;
                        TimeSpan e = sw.Elapsed;
                        TimeSpan eta = d > 0 ? TimeSpan.FromMilliseconds(e.TotalMilliseconds * (reader.Count - d) / d) : TimeSpan.Zero;
                        Console.Error.WriteLine($"  [{consoleTag}] {d}/{reader.Count} ({pct:F1}%)  elapsed={TimeFormat.Format(e)}  ETA={TimeFormat.Format(eta)}  successes={stats.Successes}");
                    }
                });
            }
            catch (OperationCanceledException) { /* halted by TerminateOnSuccess */ }
        }
    }
}
