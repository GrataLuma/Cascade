using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GrataCascade.Core;

namespace NistTests
{
    /// <summary>
    /// Repeatedly runs the Grata Cascade protocol and collects shared keys K*.
    /// Mirrors the logic of the original Form1.button2_Click loop.
    /// </summary>
    public sealed class KeyCollector
    {
        public sealed class Options
        {
            public int Runs { get; set; } = 10000;
            public int MaxIterations { get; set; } = 5000;
            public int? Seed { get; set; } = null;
            public bool SuppressProtocolOutput { get; set; } = true;
        }

        public sealed class Result
        {
            public byte[][] Keys;
            public int SuccessfulRuns;
            public int FailedRuns;
            public int NoFinalRuns;
            public TimeSpan Elapsed;
            public double AvgMsPerRun;
            public int AvgIterationsPerRun;
        }

        /// <summary>
        /// Post-F1 NO-OP. Pre-F1 this used reflection to swap the singleton's
        /// internal System.Random with a seeded instance for reproducible key
        /// collection. F1 migrated SecureRandom to RandomNumberGenerator (crypto
        /// PRNG) which cannot be seeded; deterministic key banks are no longer
        /// reproducible. The --seed CLI flag is accepted for back-compat and
        /// forwarded here, but has no effect on the generated keys.
        /// </summary>
        public static void SeedSecureRandom(int seed)
        {
            SecureRandom.Instance.SetRandomSeed(seed);
        }

        public Result Collect(Options opts, Action<int, int, TimeSpan> progress = null)
        {
            if (opts.Seed.HasValue) SeedSecureRandom(opts.Seed.Value);

            var keys = new byte[opts.Runs][];
            int success = 0, failed = 0, noFinal = 0;
            long totalIterations = 0;

            TextWriter originalOut = Console.Out;
            if (opts.SuppressProtocolOutput) Console.SetOut(TextWriter.Null);

            var sw = Stopwatch.StartNew();
            try
            {
                for (int m = 0; m < opts.Runs; m++)
                {
                    var clientA = new ClientA();
                    var clientB = new ClientB();

                    int i = 1;
                    bool finalHit = false;

                    for (; i <= opts.MaxIterations; i++)
                    {
                        var msgToB = clientA.CreateMessageToB();
                        var msgToA = clientB.ProcessMessageFromA(msgToB, i);
                        clientA.ProcessMessageFromB(msgToA, i);

                        if (msgToA.Tag != null && msgToA.Tag.Contains("Final"))
                        {
                            finalHit = true;
                            break;
                        }
                    }

                    totalIterations += i;

                    if (!finalHit)
                    {
                        noFinal++;
                        keys[m] = null;
                        continue;
                    }

                    byte[] passA = clientA.GetPassword();
                    byte[] passB = clientB.GetPassword();

                    if (ByteArrayEquals(passA, passB) && passA.Length == 32)
                    {
                        keys[m] = passA;
                        success++;
                    }
                    else
                    {
                        keys[m] = null;
                        failed++;
                    }

                    if (progress != null && ((m + 1) % Math.Max(1, opts.Runs / 100) == 0 || m + 1 == opts.Runs))
                    {
                        progress(m + 1, opts.Runs, sw.Elapsed);
                    }
                }
            }
            finally
            {
                if (opts.SuppressProtocolOutput) Console.SetOut(originalOut);
                sw.Stop();
            }

            return new Result
            {
                Keys = keys,
                SuccessfulRuns = success,
                FailedRuns = failed,
                NoFinalRuns = noFinal,
                Elapsed = sw.Elapsed,
                AvgMsPerRun = opts.Runs == 0 ? 0 : sw.Elapsed.TotalMilliseconds / opts.Runs,
                AvgIterationsPerRun = opts.Runs == 0 ? 0 : (int)(totalIterations / opts.Runs)
            };
        }

        private static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Binary format: [int32 count][int32 keyLength][count * keyLength bytes].
        /// Failed runs (null keys) are skipped — only successful K* values are serialized.
        /// </summary>
        public static void SaveToFile(string path, byte[][] keys)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            int count = 0;
            int keyLength = 32;
            foreach (var k in keys)
            {
                if (k != null)
                {
                    count++;
                    keyLength = k.Length;
                }
            }

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(count);
            bw.Write(keyLength);
            foreach (var k in keys)
            {
                if (k == null) continue;
                bw.Write(k, 0, k.Length);
            }
        }

        public static byte[][] LoadFromFile(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            int count = br.ReadInt32();
            int keyLength = br.ReadInt32();
            var keys = new byte[count][];
            for (int i = 0; i < count; i++) keys[i] = br.ReadBytes(keyLength);
            return keys;
        }

        public static string ComputeFileSha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash);
        }
    }
}
