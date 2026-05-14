using System;
using System.Collections.Generic;
using GrataCascade.Core;
using HashFirstAttacker.Cli;
using HashFirstAttacker.Commands;

namespace HashFirstAttacker
{
    /// <summary>
    /// Entry point for the HashFirstAttacker CLI. After R1 refactor, this file
    /// is intentionally thin — it owns only the dispatch table, the
    /// <see cref="ActiveConfig"/> singleton, and <see cref="LoadCfg"/> (called
    /// by every protocol-running subcommand to populate
    /// <see cref="GrataCascade.Core.Configuration"/>'s static fields and stash
    /// the loaded config for report headers).
    ///
    /// Subcommand bodies live in <c>Commands/*Commands.cs</c>; CLI parsing
    /// helpers in <c>Cli/ArgParser.cs</c>; help text in <c>Cli/HelpText.cs</c>;
    /// time formatting in <c>Cli/TimeFormat.cs</c>.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// F9.d: the config loaded for the current process. Populated by
        /// <see cref="LoadCfg"/> at the top of each protocol-running subcommand.
        /// Report writers read <see cref="ProtocolConfiguration.OriginalJson"/>
        /// to embed in report headers.
        /// </summary>
        public static ProtocolConfiguration ActiveConfig;

        public static ProtocolConfiguration LoadCfg(Dictionary<string, string> parsed)
        {
            string explicitPath = parsed.TryGetValue("--config", out var cp) ? cp : null;
            var cfg = ConfigLoader.Load(explicitPath);
            ActiveConfig = cfg;
            Console.Error.WriteLine(
                $"Config: {cfg.OriginPath} (name={cfg.Name}, VL={cfg.VectorLength}, VC={cfg.VectorCount}, " +
                $"hashes={cfg.HashLengthAtoB}/{cfg.HashLengthBtoA}/{cfg.HashLengthPassword}, " +
                $"p_upd={cfg.UpdateProbability})");
            return cfg;
        }

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                HelpText.Print();
                return 0;
            }

            try
            {
                return args[0] switch
                {
                    "smoke"               => DiagnosticCommands.RunSmoke(args),
                    "b1"                  => AttackerBatteryCommands.RunB1(args),
                    "b2"                  => AttackerBatteryCommands.RunB2(args),
                    "b3"                  => AttackerBatteryCommands.RunB3(args),
                    "b4"                  => AttackerBatteryCommands.RunB4(args),
                    "b5"                  => AttackerBatteryCommands.RunB5(args),
                    "b5-extended"         => AttackerBatteryCommands.RunB5Extended(args),
                    "b6"                  => AttackerBatteryCommands.RunB6(args),
                    "cp_test"             => Reports.ClopperPearson.RunSelfTest(),
                    "verify-sha"          => ShaCommands.RunVerifySha(args),
                    "verify-sha-unit"     => ShaCommands.RunVerifyShaUnit(args),
                    "bench-sha"           => ShaCommands.RunBenchSha(args),
                    "convergence"         => DiagnosticCommands.RunConvergence(args),
                    "convergence-summary" => DiagnosticCommands.RunConvergenceSummary(args),
                    "calibrate-f4"        => CalibrationCommands.RunCalibrateF4(args),
                    "calibrate-p-upd"     => CalibrationCommands.RunCalibratePUpd(args),
                    "verify-config"       => DiagnosticCommands.RunVerifyConfig(args),
                    "dump-probs"          => DiagnosticCommands.RunDumpProbs(args),
                    _                     => UnknownCommand(args[0])
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static int UnknownCommand(string arg0)
        {
            Console.Error.WriteLine($"Unknown command: {arg0}. Use --help.");
            return 2;
        }
    }
}
