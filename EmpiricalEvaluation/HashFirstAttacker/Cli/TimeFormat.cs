using System;

namespace HashFirstAttacker.Cli
{
    /// <summary>
    /// Compact human-readable elapsed-time formatting used across the CLI
    /// (progress bars, ETA, summaries). Was previously a static helper on
    /// <see cref="HashFirstAttacker.Attackers.BaselineRandomAttacker"/>;
    /// moved here in R1 refactor since it's a CLI concern, not attacker-specific.
    /// </summary>
    public static class TimeFormat
    {
        public static string Format(TimeSpan ts)
        {
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d{ts.Hours:D2}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m{ts.Seconds:D2}s";
            return $"{ts.TotalSeconds:F1}s";
        }
    }
}
