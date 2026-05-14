using System;
using System.Collections.Generic;
using System.IO;

namespace GrataCascade.Core
{
    /// <summary>
    /// F9.d: centralized resolution of the --config argument for all CLI modules.
    /// Encapsulates the fallback chain for the "no --config given" case and the
    /// post-load call to <see cref="Configuration.LoadFrom"/>.
    ///
    /// Fallback chain (F9.a §3 + user-prompt §5):
    ///   1. {CWD}/configs/reference.json
    ///   2. {exe_dir}/configs/reference.json
    ///   3. walk up from CWD up to 5 levels, trying configs/reference.json each step
    /// If none exist, throws <see cref="FileNotFoundException"/> with the list of
    /// paths tried. The built-in <see cref="ProtocolConfiguration.Reference"/> is
    /// NOT used as an implicit fallback — per F9.a review, reports must reference
    /// a file on disk for full auditability.
    /// </summary>
    public static class ConfigLoader
    {
        public const string DefaultConfigFileName = "reference.json";
        public const string DefaultConfigDir = "configs";
        private const int WalkUpMaxLevels = 5;

        /// <summary>
        /// Load the config specified by <paramref name="explicitPath"/>, or resolve
        /// the default reference config if null/empty. On success, applies the
        /// config to the legacy <see cref="Configuration"/> statics via
        /// <see cref="Configuration.LoadFrom"/> so protocol code picks up new values.
        /// </summary>
        public static ProtocolConfiguration Load(string explicitPath)
        {
            string path;
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (!File.Exists(explicitPath))
                    throw new FileNotFoundException(
                        $"--config path does not exist: {explicitPath}", explicitPath);
                path = explicitPath;
            }
            else
            {
                path = ResolveDefault();
                if (path == null)
                {
                    var tried = new List<string>();
                    foreach (var p in EnumerateCandidates()) tried.Add(p);
                    throw new FileNotFoundException(
                        $"No --config provided and no default '{DefaultConfigDir}/{DefaultConfigFileName}' " +
                        $"was found. Paths tried (in order):\n  " + string.Join("\n  ", tried) +
                        $"\nProvide --config <path> explicitly, or create '{DefaultConfigDir}/{DefaultConfigFileName}' " +
                        $"in the current working directory.");
                }
            }

            var cfg = ProtocolConfiguration.LoadFromJson(path);
            Configuration.LoadFrom(cfg);
            return cfg;
        }

        private static string ResolveDefault()
        {
            foreach (var p in EnumerateCandidates())
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            string cwd = Directory.GetCurrentDirectory();
            yield return Path.Combine(cwd, DefaultConfigDir, DefaultConfigFileName);

            string exeDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(exeDir) &&
                !string.Equals(Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar),
                               Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar),
                               StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(exeDir, DefaultConfigDir, DefaultConfigFileName);
            }

            string dir = cwd;
            for (int i = 0; i < WalkUpMaxLevels; i++)
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) yield break;
                dir = parent.FullName;
                yield return Path.Combine(dir, DefaultConfigDir, DefaultConfigFileName);
            }
        }

        /// <summary>
        /// Build a Markdown code-fence block embedding the full JSON body of
        /// <paramref name="cfg"/> for inclusion in report headers (F9.d auditability).
        /// </summary>
        public static string BuildReportHeader(ProtocolConfiguration cfg)
        {
            if (cfg == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("### Configuration");
            sb.AppendLine();
            sb.AppendLine($"**Origin:** `{cfg.OriginPath}`  **name:** `{cfg.Name}`  **schema:** `{cfg.SchemaVersion}`");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.Append(cfg.OriginalJson.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
