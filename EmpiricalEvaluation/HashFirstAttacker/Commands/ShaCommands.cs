using HashFirstAttacker.Cli;

namespace HashFirstAttacker.Commands
{
    /// <summary>
    /// SHA-tooling subcommands (F3-era: golden-reference verification, unit
    /// parity test, microbenchmark). All thin wrappers over the existing
    /// `*Command` static classes.
    /// </summary>
    public static class ShaCommands
    {
        public static int RunVerifySha(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            string mode = parsed.TryGetValue("--mode", out var m) ? m : "verify";
            int runs = ArgParser.GetInt(parsed, "--runs", 1000);
            int seed = ArgParser.GetInt(parsed, "--seed", 42);
            string path = parsed.TryGetValue("--path", out var p) ? p : "reports/golden/sha256_reference_runs.json";
            return VerifyShaCommand.Run(mode, runs, seed, path);
        }

        public static int RunVerifyShaUnit(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            int iters = ArgParser.GetInt(parsed, "--iters", 10000);
            int? seed = parsed.TryGetValue("--seed", out var s) ? int.Parse(s) : (int?)null;
            return Sha256ParityTest.Run(iters, seed);
        }

        public static int RunBenchSha(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            int duration = ArgParser.GetInt(parsed, "--seconds", 5);
            int protocolRuns = ArgParser.GetInt(parsed, "--protocol-runs", 0);
            int protocolSeed = ArgParser.GetInt(parsed, "--seed", 42);
            return BenchShaCommand.Run(duration, protocolRuns, protocolSeed);
        }
    }
}
