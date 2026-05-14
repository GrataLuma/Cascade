using System;
using System.Collections.Generic;

namespace HashFirstAttacker.Cli
{
    /// <summary>
    /// CLI argument parsing helpers shared across all subcommands. Pattern:
    /// <c>--key value</c> pairs, plus boolean flags <c>--flag</c> stored as
    /// the literal string "true". Argument zero (the subcommand name) is
    /// skipped by the caller; ParseArgs starts at index 1.
    /// </summary>
    public static class ArgParser
    {
        public static Dictionary<string, string> ParseArgs(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--")) continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { d[a] = args[i + 1]; i++; }
                else d[a] = "true";
            }
            return d;
        }

        public static int GetInt(Dictionary<string, string> d, string key, int def)
            => d.TryGetValue(key, out var v) ? int.Parse(v) : def;

        public static long GetLong(Dictionary<string, string> d, string key, long def)
            => d.TryGetValue(key, out var v) ? long.Parse(v) : def;
    }
}
