using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// F3.c end-to-end protocol equivalence check.
    ///
    /// `--mode record`: seeds SecureRandom with a fixed seed, drives ProtocolRunner
    /// N times, and records (iterations, K*_hex, transcript_digest_hex) into a
    /// golden-reference JSON. Intended to be run PRE-migration and committed.
    ///
    /// `--mode verify`: re-runs the same N seeded protocol runs and compares each
    /// record to the committed golden file byte-by-byte. Bit-exact match is the
    /// pass condition — SHA-256 is deterministic, so any divergence signals a
    /// migration bug.
    ///
    /// Determinism relies on SecureRandom.Instance being reseeded once at the start
    /// (via reflection into its private `random` field — same pattern as
    /// NistTests.KeyCollector.SeedSecureRandom). ProtocolRunner itself draws no
    /// random state outside SecureRandom.Instance.
    /// </summary>
    public static class VerifyShaCommand
    {
        public sealed class RunRecord
        {
            public int RunIndex { get; set; }
            public int Iterations { get; set; }
            public bool FinalHit { get; set; }
            public bool KeysMatch { get; set; }
            public string KStarHex { get; set; }
            public string TranscriptDigestHex { get; set; }
        }

        public sealed class GoldenFile
        {
            public string Version { get; set; } = "1.0";
            public int Seed { get; set; }
            public int Runs { get; set; }
            public string ProtocolConfigHash { get; set; }
            public string GeneratedUtc { get; set; }
            public List<RunRecord> Records { get; set; } = new List<RunRecord>();
        }

        public static int Run(string mode, int runs, int seed, string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = "reports/golden/sha256_reference_runs.json";

            Console.Error.WriteLine($"verify-sha: mode={mode}, seed={seed}, runs={runs}, path={outputPath}");

            SeedSecureRandom(seed);

            var cfgHash = ProtocolConfigHash();
            var golden = new GoldenFile
            {
                Seed = seed,
                Runs = runs,
                ProtocolConfigHash = cfgHash,
                GeneratedUtc = DateTime.UtcNow.ToString("o")
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
            {
                var outcome = new ProtocolRunner().Run();
                var rec = new RunRecord
                {
                    RunIndex = i,
                    Iterations = outcome.Iterations,
                    FinalHit = outcome.FinalHit,
                    KeysMatch = outcome.KeysMatch,
                    KStarHex = outcome.KeyA != null ? Convert.ToHexString(outcome.KeyA) : "",
                    TranscriptDigestHex = Convert.ToHexString(ComputeTranscriptDigest(outcome))
                };
                golden.Records.Add(rec);

                if ((i + 1) % Math.Max(1, runs / 20) == 0 || i + 1 == runs)
                {
                    Console.Error.Write($"\r  [{i + 1}/{runs}] elapsed={sw.Elapsed}  ");
                }
            }
            Console.Error.WriteLine();

            if (mode == "record")
            {
                EnsureDir(outputPath);
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(outputPath, JsonSerializer.Serialize(golden, opts));

                var fileHash = ComputeFileHashHex(outputPath);
                File.WriteAllText(outputPath + ".sha256", fileHash + "\n");
                Console.WriteLine($"RECORDED: {outputPath}");
                Console.WriteLine($"SHA-256:  {fileHash}");
                Console.WriteLine($"records:  {golden.Records.Count}");
                return 0;
            }

            if (mode == "verify")
            {
                if (!File.Exists(outputPath))
                {
                    Console.Error.WriteLine($"ERROR: golden file not found: {outputPath}. Run --mode record first.");
                    return 2;
                }

                var expected = JsonSerializer.Deserialize<GoldenFile>(File.ReadAllText(outputPath));
                if (expected.Runs != golden.Runs || expected.Seed != golden.Seed)
                {
                    Console.Error.WriteLine($"ERROR: parameter mismatch. Expected runs={expected.Runs} seed={expected.Seed}, got runs={golden.Runs} seed={golden.Seed}.");
                    return 2;
                }

                int mismatches = 0;
                for (int i = 0; i < runs; i++)
                {
                    var e = expected.Records[i];
                    var a = golden.Records[i];
                    if (e.Iterations != a.Iterations ||
                        e.FinalHit != a.FinalHit ||
                        e.KeysMatch != a.KeysMatch ||
                        !string.Equals(e.KStarHex, a.KStarHex, StringComparison.Ordinal) ||
                        !string.Equals(e.TranscriptDigestHex, a.TranscriptDigestHex, StringComparison.Ordinal))
                    {
                        mismatches++;
                        if (mismatches <= 3)
                        {
                            Console.Error.WriteLine($"  MISMATCH run={i}");
                            Console.Error.WriteLine($"    expected: iters={e.Iterations} K*={e.KStarHex.Substring(0, Math.Min(16, e.KStarHex.Length))}... digest={e.TranscriptDigestHex.Substring(0, 16)}...");
                            Console.Error.WriteLine($"    actual  : iters={a.Iterations} K*={a.KStarHex.Substring(0, Math.Min(16, a.KStarHex.Length))}... digest={a.TranscriptDigestHex.Substring(0, 16)}...");
                        }
                    }
                }

                if (mismatches == 0)
                {
                    Console.WriteLine($"VERIFY OK: {runs} runs, bit-exact match against {outputPath}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"VERIFY FAIL: {mismatches}/{runs} runs diverged.");
                    return 1;
                }
            }

            Console.Error.WriteLine($"ERROR: unknown mode '{mode}'. Use 'record' or 'verify'.");
            return 2;
        }

        private static void SeedSecureRandom(int seed)
        {
            // Post-F1: forwards to SecureRandom.SetRandomSeed which is a NO-OP under
            // the crypto PRNG (warn once, then ignore). This subcommand's --mode
            // verify path can no longer produce bit-exact reproducibility; it
            // remains useful as an F3 artifact and as a convergence smoke test.
            SecureRandom.Instance.SetRandomSeed(seed);
        }

        private static string ProtocolConfigHash()
        {
            var sb = new StringBuilder();
            sb.Append($"VL={Configuration.VectorLength};");
            sb.Append($"VC={Configuration.VectorCount};");
            sb.Append($"HAB={Configuration.HASHLength_AtoB};");
            sb.Append($"HBA={Configuration.HASHLength_BtoA};");
            sb.Append($"HP={Configuration.HASHLength_Password};");
            sb.Append($"M={Configuration.AESPassedVectorsCount};");
            sb.Append($"NoUpd={Configuration.NoUpdateLimit};");
            sb.Append($"TargetProb={Configuration.TargetProbability};");
            sb.Append($"SeedMinMax={Configuration.SeedMinMax}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>
        /// Deterministic hash over all public transcript bytes plus ground-truth K*.
        /// Any protocol SHA-256 divergence (wrong byte, wrong length, wrong order)
        /// flips this digest.
        /// </summary>
        private static byte[] ComputeTranscriptDigest(ProtocolRunner.Outcome outcome)
        {
            using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            if (outcome.KeyA != null) ih.AppendData(outcome.KeyA);
            ih.AppendData(BitConverter.GetBytes(outcome.Iterations));
            ih.AppendData(BitConverter.GetBytes(outcome.FinalHit));
            ih.AppendData(BitConverter.GetBytes(outcome.KeysMatch));

            var t = outcome.Transcript;
            ih.AppendData(BitConverter.GetBytes(t.Iterate.Count));
            foreach (var r in t.Iterate)
            {
                ih.AppendData(BitConverter.GetBytes(r.Tag));
                foreach (var h in r.AtoB_Hashes) ih.AppendData(h.Data);
                if (r.R != null) ih.AppendData(r.R);
                foreach (var h in r.BtoA_Hashes) ih.AppendData(h.Data);
                if (r.AesCiphertext != null) ih.AppendData(r.AesCiphertext);
            }
            if (t.Final != null)
            {
                ih.AppendData(BitConverter.GetBytes(t.Final.Tag));
                foreach (var h in t.Final.AtoB_Hashes) ih.AppendData(h.Data);
                foreach (var h in t.Final.BtoA_PasswordHashes) ih.AppendData(h.Data);
            }

            return ih.GetHashAndReset();
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static string ComputeFileHashHex(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
    }
}
