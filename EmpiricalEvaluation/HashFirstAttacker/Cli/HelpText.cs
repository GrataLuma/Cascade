using System;

namespace HashFirstAttacker.Cli
{
    /// <summary>
    /// Multi-section help text for the HashFirstAttacker CLI. Printed when
    /// the user passes no arguments, <c>--help</c>, or <c>-h</c>.
    /// </summary>
    public static class HelpText
    {
        public static void Print()
        {
            Console.WriteLine("HashFirstAttacker — empirical attacker battery against Grata Cascade");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project HashFirstAttacker -- <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Global option (all protocol commands):");
            Console.WriteLine("  --config <path>    JSON config file (F9). If omitted, resolves configs/reference.json");
            Console.WriteLine("                     via: CWD -> exe dir -> walk up 5 parents. Fails if none found.");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  smoke              Run one protocol round, print transcript stats (B.0 check)");
            Console.WriteLine("  b1                 Run Baseline Random Sampling Attacker (B.1)");
            Console.WriteLine("                       --budget-seconds <S>  time budget (default 60)");
            Console.WriteLine("  b2                 Run Hash-First Filtered Attacker (B.2)");
            Console.WriteLine("                       --pool <N>            pool size per run (default 10_000_000)");
            Console.WriteLine("                       --runs <N>            number of protocol runs to attack (default 1)");
            Console.WriteLine("                       --histogram           print per-round survivor histograms for first run");
            Console.WriteLine("  b3                 Run Structured Sampling Attacker (B.3)");
            Console.WriteLine("                       --mode <typical|tail> sampling mode (default tail)");
            Console.WriteLine("                       --pool <N>            pool size per run (default 10_000_000)");
            Console.WriteLine("                       --runs <N>            number of protocol runs to attack (default 1)");
            Console.WriteLine("  b4                 Run AES-Oracle Attacker (B.4) — measures per-round");
            Console.WriteLine("                     oracle discrimination power (TP vs FP), not full K*.");
            Console.WriteLine("                       --runs <N>            number of protocol runs (default 5)");
            Console.WriteLine("                       --disc-samples <K>    near-seed samples per test (default 50000)");
            Console.WriteLine("  b5                 Run full attacker battery and produce aggregate report");
            Console.WriteLine("                       --runs <N>            number of protocol runs (default 10)");
            Console.WriteLine("                       --pool <N>            pool size for B.2/B.3 (default 10_000_000)");
            Console.WriteLine("                       --b1-budget-seconds <S>  B.1 time budget per run (default 30)");
            Console.WriteLine("                       --b4-disc-samples <K> B.4 discriminator samples (default 100000)");
            Console.WriteLine("                       --output <path>       markdown report (default reports/attacker_report.md)");
            Console.WriteLine("                       --csv <path>          CSV results (default reports/attacker_results.csv)");
            Console.WriteLine("  b5-extended        Large parallel evaluation (N=300 default) with persistence");
            Console.WriteLine("                       --runs <N>            default 300");
            Console.WriteLine("                       --b1-budget-seconds <S>  B.1 per-transcript budget (default 1800 = 30 min)");
            Console.WriteLine("                       --pool <N>            pool B.2/B.3 (default 10^7)");
            Console.WriteLine("                       --b7-pool <N>         pool B.7 (default 10^6; bound dedup memory)");
            Console.WriteLine("                       --b4-disc-samples <K> default 10^6");
            Console.WriteLine("                       --workers <N>         parallel worker count (default cores-1)");
            Console.WriteLine("                       --base-seed <int>     seed base for reproducibility");
            Console.WriteLine("                       --resume              continue from progress.csv");
            Console.WriteLine("                       --overwrite           delete progress.csv and start over");
            Console.WriteLine("                       --skip-gen            reuse existing transcripts bin");
            Console.WriteLine("                       --output --csv --progress  output paths");
            Console.WriteLine("  b6                 B.6 Synthetic Flood (F5.e) per-round indistinguishability test");
            Console.WriteLine("                       --k <N>               candidates per slot (default 2)");
            Console.WriteLine("                       --runs <N>            transcripts (default 1)");
            Console.WriteLine("                       --max-rounds <N>      cap rounds per transcript (0=all)");
            Console.WriteLine("                       --output-csv <path>   per-round CSV output");
            Console.WriteLine("  cp_test            Clopper-Pearson CI self-test");
            Console.WriteLine("  verify-sha         F3 end-to-end SHA migration golden-reference check");
            Console.WriteLine("                       --mode <record|verify>   default verify");
            Console.WriteLine("                       --runs <N>                default 1000");
            Console.WriteLine("                       --seed <int>              default 42");
            Console.WriteLine("                       --path <file>             default reports/golden/sha256_reference_runs.json");
            Console.WriteLine("  verify-sha-unit    F3 unit parity test: SHA256Managed vs SHA256.HashData");
            Console.WriteLine("                       --iters <N>               default 10000");
            Console.WriteLine("                       --seed <int>              optional, for determinism");
            Console.WriteLine("  bench-sha          F3 microbenchmark: 32B hash throughput per API variant");
            Console.WriteLine("                       --seconds <S>             default 5");
            Console.WriteLine("                       --protocol-runs <N>       optional macro: N protocol runs (no longer deterministic post-F1)");
            Console.WriteLine("                       --seed <int>              accepted for CLI compat; NO-OP post-F1 (crypto RNG)");
            Console.WriteLine("  convergence        F1.b convergence pilot under crypto RNG");
            Console.WriteLine("                       --runs <N>                default 1000");
            Console.WriteLine("                       --output <path>           default reports/convergence_post_f1.md");
            Console.WriteLine("                       --csv <path>              optional per-run CSV");
            Console.WriteLine("                       --flush-every <N>         CSV flush cadence (default 1000)");
            Console.WriteLine("                       --max-iter <N>            override Configuration.MaxIterations");
            Console.WriteLine("  convergence-summary  Aggregator: read convergence CSV → markdown summary");
            Console.WriteLine("                       --csv <path>              required, must exist");
            Console.WriteLine("                       --output <path>           default reports/convergence_summary.md");
            Console.WriteLine("  dump-probs         Per-round probability dump (passedVectors + passwordVectors)");
            Console.WriteLine("                       --runs <N>                default 10");
            Console.WriteLine("                       --output <path>           CSV (default timestamped)");
            Console.WriteLine("  calibrate-p-upd    F10.a sweep of p_upd (vector update probability)");
            Console.WriteLine("                       --p-upd-values \"0.1,...\"  default 0.1,0.25,0.5,0.75,0.9,0.95,1.0");
            Console.WriteLine("                       --runs <N>                default 200");
            Console.WriteLine("                       --max-iter / --early-abort-window / --memory-guard-mb   same as calibrate-f4");
            Console.WriteLine("                       --candidate-max <v|off>   override CandidateMaxProbabilityLog2 for the sweep (F10.d ablation)");
            Console.WriteLine("                       --cut-limit <v|off>       override CutLimitProbabilityLog2 for the sweep");
            Console.WriteLine("                       --output <path>           default reports/p_upd_calibration.md");
            Console.WriteLine("  calibrate-f4       F4.c probability-threshold calibration");
            Console.WriteLine("                       --runs <N>                runs per combo, default 200");
            Console.WriteLine("                       --workers <N>             >1 = orchestrator spawns N child processes, default 1");
            Console.WriteLine("                       --output <path>           markdown, default reports/probability_thresholds_calibration.md");
            Console.WriteLine("                       --output-json <path>      optional JSON (worker mode: written alongside markdown)");
            Console.WriteLine("                       --combos \"c1:m1,c2:m2\"    explicit combo list (highest precedence)");
            Console.WriteLine("                       --cut-values \"-8,-16,..\"  CutLimit values (cartesian with cand-values)");
            Console.WriteLine("                       --cand-values \"-16,-32,.\" CandidateMax values; use 'off' for -Infinity");
            Console.WriteLine("                       --max-iter <N>            MaxIterations cap per protocol run, default = active config (reference v2: 500)");
            Console.WriteLine("                       --early-abort-window <N>  abort combo after N runs if <25% convergence, default 50");
            Console.WriteLine("                       --memory-guard-mb <MB>    abort combo if worker WS exceeds MB, default 2048");
            Console.WriteLine("  verify-config      F9.b/d config loader smoke test");
            Console.WriteLine("                       --config <path>       optional; else resolves default");
            Console.WriteLine();
            Console.WriteLine("  --help, -h         Show this help");
        }
    }
}
