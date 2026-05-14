using GrataCascade.Core;
using HashFirstAttacker.Cli;

namespace HashFirstAttacker.Commands
{
    /// <summary>
    /// F4 / F10 calibration subcommands. Both wrap their respective
    /// `Calibrate*Command` static classes; the only logic here is parsing
    /// optional sweep-overrides.
    /// </summary>
    public static class CalibrationCommands
    {
        public static int RunCalibrateF4(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            var opts = new CalibrateF4Command.Options
            {
                Runs = ArgParser.GetInt(parsed, "--runs", 200),
                Workers = ArgParser.GetInt(parsed, "--workers", 1),
                MaxIterations = ArgParser.GetInt(parsed, "--max-iter", Configuration.MaxIterations),
                EarlyAbortWindow = ArgParser.GetInt(parsed, "--early-abort-window", 50),
                MemoryGuardBytes = (long)ArgParser.GetInt(parsed, "--memory-guard-mb", 2048) * 1024L * 1024L,
                OutputMarkdown = parsed.TryGetValue("--output", out var o) ? o : "reports/probability_thresholds_calibration.md",
                OutputJson = parsed.TryGetValue("--output-json", out var oj) ? oj : null,
                ConfigPath = parsed.TryGetValue("--config", out var cpx) ? cpx : null
            };

            // Combo source precedence: --combos > (--cut-values × --cand-values) > defaults.
            if (parsed.TryGetValue("--combos", out var combosArg))
            {
                opts.Combos = CalibrateF4Command.ParseComboList(combosArg);
            }
            else if (parsed.TryGetValue("--cut-values", out var cutArg) &&
                     parsed.TryGetValue("--cand-values", out var candArg))
            {
                var cuts = CalibrateF4Command.ParseDoubleList(cutArg);
                var cands = CalibrateF4Command.ParseDoubleList(candArg);
                opts.Combos = CalibrateF4Command.CartesianCombos(cuts, cands);
            }

            // Worker mode: suppress markdown unless explicitly requested. Keeps
            // orchestrator output clean.
            if (!string.IsNullOrEmpty(opts.OutputJson) && !parsed.ContainsKey("--output"))
            {
                opts.OutputMarkdown = null;
            }

            return CalibrateF4Command.Run(opts);
        }

        public static int RunCalibratePUpd(string[] args)
        {
            var parsed = ArgParser.ParseArgs(args);
            Program.LoadCfg(parsed);
            var opts = new CalibratePUpdCommand.Options
            {
                Runs = ArgParser.GetInt(parsed, "--runs", 200),
                MaxIterations = ArgParser.GetInt(parsed, "--max-iter", Configuration.MaxIterations),
                EarlyAbortWindow = ArgParser.GetInt(parsed, "--early-abort-window", 50),
                MemoryGuardBytes = (long)ArgParser.GetInt(parsed, "--memory-guard-mb", 2048) * 1024L * 1024L,
                OutputMarkdown = parsed.TryGetValue("--output", out var o) ? o : "reports/p_upd_calibration.md",
                OutputJson = parsed.TryGetValue("--output-json", out var oj) ? oj : null
            };
            if (parsed.TryGetValue("--p-upd-values", out var pv))
            {
                opts.PUpdValues = CalibratePUpdCommand.ParseDoubleList(pv);
            }
            if (parsed.TryGetValue("--candidate-max", out var cm))
            {
                opts.CandidateMaxOverride = CalibratePUpdCommand.ParseThresholdOrOff(cm);
            }
            if (parsed.TryGetValue("--cut-limit", out var cl))
            {
                opts.CutLimitOverride = CalibratePUpdCommand.ParseThresholdOrOff(cl);
            }
            return CalibratePUpdCommand.Run(opts);
        }
    }
}
