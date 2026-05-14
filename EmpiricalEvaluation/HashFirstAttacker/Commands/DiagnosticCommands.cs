using System;
using GrataCascade.Core;
using HashFirstAttacker.Cli;

namespace HashFirstAttacker.Commands
{
    /// <summary>
    /// Diagnostic / one-shot subcommands: smoke, convergence, dump-probs,
    /// verify-config. Most are thin wrappers over their `*Command` static
    /// implementations.
    /// </summary>
    public static class DiagnosticCommands
    {
        public static int RunSmoke(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            Console.WriteLine("Running one protocol round and printing transcript stats...");
            var runner = new ProtocolRunner();
            var outcome = runner.Run();

            Console.WriteLine($"Iterations run       : {outcome.Iterations}");
            Console.WriteLine($"Final hit            : {outcome.FinalHit}");
            Console.WriteLine($"Keys match           : {outcome.KeysMatch}");
            Console.WriteLine($"Transcript.Iterate rounds: {outcome.Transcript.Iterate.Count}");
            Console.WriteLine($"Transcript.Final slots   : {outcome.Transcript.Final?.BtoA_PasswordHashes.Count}");
            Console.WriteLine($"Parameters           : VL={outcome.Transcript.VectorLength} VC={outcome.Transcript.VectorCount} " +
                              $"H_AtoB={outcome.Transcript.HashLenAtoB} H_BtoA={outcome.Transcript.HashLenBtoA} " +
                              $"H_PW={outcome.Transcript.HashLenPassword} Slots={outcome.Transcript.PasswordSlots}");
            if (outcome.KeysMatch)
            {
                Console.WriteLine($"K* = {BitConverter.ToString(outcome.KeyA).Replace("-", "")}");
            }
            return outcome.KeysMatch ? 0 : 3;
        }

        public static int RunConvergence(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int runs = ArgParser.GetInt(parsed, "--runs", 1000);
            string output = parsed.TryGetValue("--output", out var o) ? o : "reports/convergence_post_f1.md";
            string csv = parsed.TryGetValue("--csv", out var c) ? c : null;
            int flushEvery = ArgParser.GetInt(parsed, "--flush-every", 1000);
            int maxIter = ArgParser.GetInt(parsed, "--max-iter", Configuration.MaxIterations);
            return ConvergenceCommand.Run(runs, output, csv, flushEvery, maxIter);
        }

        public static int RunConvergenceSummary(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            string csv = parsed.TryGetValue("--csv", out var c) ? c : null;
            string output = parsed.TryGetValue("--output", out var o) ? o : "reports/convergence_summary.md";
            if (string.IsNullOrEmpty(csv) || !System.IO.File.Exists(csv))
            {
                Console.Error.WriteLine("ERROR: --csv <path> is required and the file must exist.");
                return 2;
            }
            return ConvergenceSummaryCommand.Run(csv, output);
        }

        public static int RunDumpProbs(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            int runs = ArgParser.GetInt(parsed, "--runs", 10);
            string output = parsed.TryGetValue("--output", out var o) ? o : null;
            return DumpProbsCommand.Run(runs, output);
        }

        public static int RunVerifyConfig(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            var cfg = ConfigLoader.Load(parsed.TryGetValue("--config", out var p) ? p : null);
            Program.ActiveConfig = cfg;
            Console.WriteLine($"schema_version            : {cfg.SchemaVersion}");
            Console.WriteLine($"name                      : {cfg.Name}");
            Console.WriteLine($"description               : {cfg.Description}");
            Console.WriteLine($"vector_length             : {cfg.VectorLength}");
            Console.WriteLine($"vector_count              : {cfg.VectorCount}");
            Console.WriteLine($"hash_length_ab/ba/pw      : {cfg.HashLengthAtoB}/{cfg.HashLengthBtoA}/{cfg.HashLengthPassword}");
            Console.WriteLine($"seed_min_max              : {cfg.SeedMinMax}");
            Console.WriteLine($"aes_passed_vectors_count  : {cfg.AesPassedVectorsCount}");
            Console.WriteLine($"update_probability        : {cfg.UpdateProbability} (NoUpdateLimit={cfg.NoUpdateLimit})");
            Console.WriteLine($"termination_threshold_log2: {cfg.TerminationThresholdLog2}");
            Console.WriteLine($"cut_limit_probability_log2: {cfg.CutLimitProbabilityLog2}");
            Console.WriteLine($"candidate_max_prob_log2   : {cfg.CandidateMaxProbabilityLog2}");
            Console.WriteLine($"cut_limit_safety_attempts : {cfg.CutLimitSafetyAttempts}");
            Console.WriteLine($"rng.type / rng.seed       : {cfg.RngType} / {(cfg.RngSeed.HasValue ? cfg.RngSeed.Value.ToString() : "null")}");
            Console.WriteLine($"origin                    : {cfg.OriginPath}");
            Console.WriteLine("OK — config validates and loads cleanly.");
            return 0;
        }
    }
}
