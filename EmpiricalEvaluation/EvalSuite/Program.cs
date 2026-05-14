using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EvalSuite
{
    public static class Program
    {
        // Subcommands routed to HashFirstAttacker.exe
        private static readonly HashSet<string> AttackerCommands = new(StringComparer.Ordinal)
        {
            "smoke", "convergence", "convergence-summary",
            "calibrate-f4", "calibrate-p-upd",
            "verify-config", "verify-sha", "verify-sha-unit", "bench-sha", "cp_test",
            "b1", "b2", "b3", "b4", "b5", "b5-extended", "b6"
        };

        // Subcommands routed to NistTests.exe
        private static readonly HashSet<string> NistCommands = new(StringComparer.Ordinal)
        {
            "nist-collect", "nist-run", "nist-selftest", "nist-reftest"
        };

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0];
            string[] rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);

            try
            {
                if (command == "list") return ListCommands();
                if (command == "paper-reproduce") return PaperReproduce(rest);

                if (AttackerCommands.Contains(command))
                {
                    return Forward("HashFirstAttacker", Prepend(command, rest));
                }

                if (NistCommands.Contains(command))
                {
                    string nistSub = command.Substring("nist-".Length);
                    return Forward("NistTests", Prepend(nistSub, rest));
                }

                Console.Error.WriteLine($"Unknown command: {command}. Try 'EvalSuite list' or 'EvalSuite --help'.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static int Forward(string toolName, string[] forwardedArgs)
        {
            string baseDir = AppContext.BaseDirectory;
            string exeName = OperatingSystem.IsWindows() ? toolName + ".exe" : toolName;
            string exe = Path.Combine(baseDir, exeName);
            if (!File.Exists(exe))
            {
                Console.Error.WriteLine($"Tool not found at expected location: {exe}");
                Console.Error.WriteLine("EvalSuite expects sibling executables in the same directory.");
                return 1;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false
            };
            foreach (var a in forwardedArgs) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            proc.WaitForExit();
            return proc.ExitCode;
        }

        private static int PaperReproduce(string[] args)
        {
            string outputDir = "reports/repro";
            string config = "configs/reference.json";
            int convRuns = 100;
            int nistKeys = 100;
            int b1BudgetSec = 60;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--output-dir" && i + 1 < args.Length) { outputDir = args[++i]; }
                else if (args[i] == "--config" && i + 1 < args.Length) { config = args[++i]; }
                else if (args[i] == "--convergence-runs" && i + 1 < args.Length) { convRuns = int.Parse(args[++i]); }
                else if (args[i] == "--nist-keys" && i + 1 < args.Length) { nistKeys = int.Parse(args[++i]); }
                else if (args[i] == "--b1-budget" && i + 1 < args.Length) { b1BudgetSec = int.Parse(args[++i]); }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    PrintReproduceHelp();
                    return 0;
                }
            }

            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"=== Paper reproduction sequence ===");
            Console.WriteLine($"Config: {config}");
            Console.WriteLine($"Output: {outputDir}");
            Console.WriteLine();

            int rc;

            Console.WriteLine($"[1/3] Convergence test ({convRuns} runs)");
            rc = Forward("HashFirstAttacker", new[]
            {
                "convergence", "--config", config,
                "--runs", convRuns.ToString(),
                "--output", Path.Combine(outputDir, "convergence.md")
            });
            if (rc != 0) { Console.Error.WriteLine($"FAILED: convergence (exit {rc})"); return rc; }

            Console.WriteLine();
            Console.WriteLine($"[2/3] NIST battery ({nistKeys} keys)");
            rc = Forward("NistTests", new[]
            {
                "run", "--config", config,
                "--runs", nistKeys.ToString(),
                "--output", Path.Combine(outputDir, "nist.md"),
                "--csv", Path.Combine(outputDir, "nist.csv")
            });
            if (rc != 0) { Console.Error.WriteLine($"FAILED: nist (exit {rc})"); return rc; }

            Console.WriteLine();
            Console.WriteLine($"[3/3] Attacker B.1 baseline (budget {b1BudgetSec}s)");
            rc = Forward("HashFirstAttacker", new[]
            {
                "b1", "--config", config,
                "--budget-seconds", b1BudgetSec.ToString()
            });
            if (rc != 0) { Console.Error.WriteLine($"FAILED: b1 (exit {rc})"); return rc; }

            Console.WriteLine();
            Console.WriteLine($"=== Reproduction complete. Results in {outputDir} ===");
            return 0;
        }

        private static int ListCommands()
        {
            Console.WriteLine("Protocol commands:");
            Console.WriteLine("  smoke                    Quick smoke test of protocol");
            Console.WriteLine("  convergence              Convergence test, N runs (use --csv for per-run logging)");
            Console.WriteLine("  convergence-summary      Aggregate per-run convergence CSV into paper-grade summary");
            Console.WriteLine("  calibrate-f4             Calibrate F4 thresholds (CutLimit, CandidateMax)");
            Console.WriteLine("  calibrate-p-upd          Calibrate p_upd parameter");
            Console.WriteLine("  verify-config            Validate a JSON config file");
            Console.WriteLine("  verify-sha               Verify SHA-256 implementation against golden refs");
            Console.WriteLine("  bench-sha                Benchmark SHA-256 throughput");
            Console.WriteLine();
            Console.WriteLine("Attackers:");
            Console.WriteLine("  b1                       B.1 Baseline random");
            Console.WriteLine("  b2                       B.2 Hash-first filtered");
            Console.WriteLine("  b3                       B.3 Structured sampling");
            Console.WriteLine("  b4                       B.4 AES oracle (discriminator)");
            Console.WriteLine("  b5                       B.5 Big-eval (single config)");
            Console.WriteLine("  b5-extended              B.5 Big-eval (extended throughput logging)");
            Console.WriteLine("  b6                       B.6 Synthetic flood (F5)");
            Console.WriteLine();
            Console.WriteLine("NIST suite:");
            Console.WriteLine("  nist-collect             Collect protocol keys to file");
            Console.WriteLine("  nist-run                 Run NIST battery on N keys");
            Console.WriteLine("  nist-selftest            Special functions self-test");
            Console.WriteLine("  nist-reftest             NIST reference vectors test");
            Console.WriteLine();
            Console.WriteLine("Orchestration:");
            Console.WriteLine("  paper-reproduce          Run standard paper-reproduction sequence");
            Console.WriteLine();
            Console.WriteLine("Use '<command> --help' for command-specific options.");
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("EvalSuite -- orchestrator for Hash-Drift CKA empirical evaluation");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  EvalSuite <command> [args...]");
            Console.WriteLine("  EvalSuite list                  List all available commands");
            Console.WriteLine("  EvalSuite paper-reproduce       Run standard reproduction sequence");
            Console.WriteLine();
            Console.WriteLine("All commands forward to sibling executables (HashFirstAttacker.exe,");
            Console.WriteLine("NistTests.exe) which can also be invoked directly.");
            Console.WriteLine();
            Console.WriteLine("See 'EvalSuite list' for the full command catalog.");
        }

        private static void PrintReproduceHelp()
        {
            Console.WriteLine("paper-reproduce -- standard reproduction sequence");
            Console.WriteLine();
            Console.WriteLine("Runs convergence test, NIST battery, and B.1 baseline attacker on a single");
            Console.WriteLine("config. Defaults are short ('smoke' scale); raise counts for full paper-grade runs.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --config <path>          Config file (default: configs/reference.json)");
            Console.WriteLine("  --output-dir <path>      Output directory (default: reports/repro)");
            Console.WriteLine("  --convergence-runs <N>   Convergence run count (default: 100)");
            Console.WriteLine("  --nist-keys <N>          NIST key count (default: 100)");
            Console.WriteLine("  --b1-budget <sec>        B.1 attacker budget seconds (default: 60)");
        }

        private static string[] Prepend(string head, string[] tail)
        {
            string[] result = new string[tail.Length + 1];
            result[0] = head;
            Array.Copy(tail, 0, result, 1, tail.Length);
            return result;
        }
    }
}
