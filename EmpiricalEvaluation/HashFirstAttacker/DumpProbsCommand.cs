using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// One-off instrumentation: run N protocol cycles end-to-end on the loaded
    /// config, capture per-round Stat-log2-probabilities of the M passedVectors
    /// (the round-level password-derivation candidates) and the cumulative
    /// passwordVectors snapshot at the Final round (the persistent vectors
    /// hashed into the K* derivation).
    ///
    /// CSV columns: run_id, round, tag, role, slot_index, probability_log2
    ///   - tag: "Iterate" or "Final"
    ///   - role: "passed" (per-round candidate, snapshot from clientB.LastPassedProbabilities)
    ///           or "password" (persistent password vector, snapshot from
    ///           clientA.passwordVectors at termination)
    ///   - slot_index: position in the per-round / per-final list
    ///   - probability_log2: Stat-log2 joint-probability (negative; more negative = rarer)
    ///
    /// Uses ClientA / ClientB directly without going through ProtocolRunner, so
    /// the loop is stripped down to what the instrumentation needs.
    /// </summary>
    public static class DumpProbsCommand
    {
        public static int Run(int runs, string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = $"reports/probs_dump_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using var csv = new StreamWriter(outputPath, append: false);
            csv.WriteLine("run_id,round,tag,role,slot_index,probability_log2");

            int maxIter = Configuration.MaxIterations;
            int finalHits = 0, keysMatch = 0;
            int totalPassedRows = 0, totalPasswordRows = 0;

            for (int run = 1; run <= runs; run++)
            {
                var clientA = new ClientA();
                var clientB = new ClientB();

                int round = 1;
                bool didFinal = false;
                for (; round <= maxIter; round++)
                {
                    MessageToClientB msgToB = clientA.CreateMessageToB();
                    MessageToClientA msgToA = clientB.ProcessMessageFromA(msgToB, round);

                    bool isFinal = msgToA.Tag != null && msgToA.Tag.Contains("Final");

                    // Snapshot per-round passedVectors probabilities (the M candidates
                    // that ClientB used to derive the round's password). Captured
                    // from the snapshot field set inside ProcessMessageFromA.
                    if (clientB.LastPassedProbabilities != null)
                    {
                        for (int i = 0; i < clientB.LastPassedProbabilities.Count; i++)
                        {
                            csv.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},{2},passed,{3},{4:G17}",
                                run, round, isFinal ? "Final" : "Iterate", i, clientB.LastPassedProbabilities[i]));
                            totalPassedRows++;
                        }
                    }

                    if (isFinal)
                    {
                        clientA.ProcessMessageFromB(msgToA, round);
                        didFinal = true;

                        // At Final, dump the cumulative passwordVectors from ClientA
                        // (these are the persistent vectors that yield K* — the
                        // "vybrané pro final password" set the user asked for).
                        if (clientA.passwordVectors != null)
                        {
                            for (int i = 0; i < clientA.passwordVectors.Count; i++)
                            {
                                var pv = clientA.passwordVectors[i];
                                csv.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "{0},{1},Final,password,{2},{3:G17}",
                                    run, round, i, pv.Probability));
                                totalPasswordRows++;
                            }
                        }
                        break;
                    }
                    else
                    {
                        clientA.ProcessMessageFromB(msgToA, round);
                    }
                }

                if (didFinal) finalHits++;
                bool km = false;
                if (didFinal)
                {
                    var ka = clientA.GetPassword();
                    var kb = clientB.GetPassword();
                    km = BytesEq(ka, kb);
                    if (km) keysMatch++;
                }

                Console.Error.WriteLine($"  [{run}/{runs}] round={round} final={didFinal} km={km}");

                if ((run % 10) == 0) csv.Flush();
            }

            csv.Flush();
            csv.Close();

            Console.WriteLine();
            Console.WriteLine($"=== dump-probs (N={runs}) ===");
            Console.WriteLine($"Final hits          : {finalHits}/{runs}");
            Console.WriteLine($"Keys match          : {keysMatch}/{runs}");
            Console.WriteLine($"CSV rows (passed)   : {totalPassedRows}");
            Console.WriteLine($"CSV rows (password) : {totalPasswordRows}");
            Console.WriteLine($"Output              : {Path.GetFullPath(outputPath)}");
            return finalHits == runs ? 0 : 3;
        }

        private static bool BytesEq(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
