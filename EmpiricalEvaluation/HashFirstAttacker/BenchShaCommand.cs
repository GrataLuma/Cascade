using System;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// F3.d microbenchmark + macrobenchmark harness.
    ///
    /// Microbench: SHA-256(32B → 32B) throughput under three APIs —
    ///   1. SHA256Managed.ComputeHash(byte[])           — legacy managed (old V2 core)
    ///   2. SHA256.HashData(byte[])                     — static, allocates output
    ///   3. SHA256.HashData(ReadOnlySpan, Span)         — static, allocation-free
    ///
    /// Macrobench (when --protocol-runs N specified): time N deterministic
    /// ProtocolRunner.Run() invocations. Lets us quantify the end-to-end wall-clock
    /// impact of the F3 migration beyond raw hash throughput (GC allocs, call
    /// overhead through SecureRandom singleton, etc.).
    /// </summary>
    public static class BenchShaCommand
    {
        public static int Run(int durationSeconds, int protocolRuns, int protocolSeed)
        {
            if (durationSeconds <= 0) durationSeconds = 5;

            byte[] input = new byte[32];
            RandomNumberGenerator.Fill(input);

            var cpuid = X86Base.CpuId(7, 0);
            bool shaNi = (cpuid.Ebx & (1 << 29)) != 0;
            Console.WriteLine($"bench-sha: input=32B, duration={durationSeconds}s per variant, SHA-NI CPUID={shaNi}");
            Console.WriteLine();

            double managed = Measure("SHA256Managed.ComputeHash(byte[])", durationSeconds, () =>
            {
#pragma warning disable SYSLIB0021
                using var sha = new SHA256Managed();
#pragma warning restore SYSLIB0021
                long iters = 0;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < durationSeconds * 1000)
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        var _ = sha.ComputeHash(input);
                    }
                    iters += 10000;
                }
                sw.Stop();
                return iters / sw.Elapsed.TotalSeconds;
            });

            double staticAlloc = Measure("SHA256.HashData(byte[])", durationSeconds, () =>
            {
                long iters = 0;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < durationSeconds * 1000)
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        var _ = SHA256.HashData(input);
                    }
                    iters += 10000;
                }
                sw.Stop();
                return iters / sw.Elapsed.TotalSeconds;
            });

            double staticSpan = Measure("SHA256.HashData(ReadOnlySpan, Span)", durationSeconds, () =>
            {
                long iters = 0;
                Span<byte> digest = stackalloc byte[32];
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < durationSeconds * 1000)
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        SHA256.HashData(input, digest);
                    }
                    iters += 10000;
                }
                sw.Stop();
                return iters / sw.Elapsed.TotalSeconds;
            });

            Console.WriteLine();
            Console.WriteLine("=== summary ===");
            Console.WriteLine($"  SHA256Managed.ComputeHash        : {managed,15:N0} ops/s  (baseline, old V2 core)");
            Console.WriteLine($"  SHA256.HashData(byte[])          : {staticAlloc,15:N0} ops/s  ({staticAlloc / managed:N2}x)");
            Console.WriteLine($"  SHA256.HashData(Span, Span)      : {staticSpan,15:N0} ops/s  ({staticSpan / managed:N2}x)");
            Console.WriteLine();
            Console.WriteLine($"Speedup (Span vs Managed)          : {staticSpan / managed:N2}x");
            if (staticSpan / managed >= 10.0)
                Console.WriteLine("→ SHA-NI active (≥10x speedup). Migration will produce major wall-clock gains.");
            else if (shaNi)
                Console.WriteLine("→ Speedup <10x despite SHA-NI CPUID support. Per-call overhead dominates at 32B input;");
            else
                Console.WriteLine("→ Speedup <10x and SHA-NI CPUID not reported. Hardware limit — document.");

            if (protocolRuns > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"=== macro: {protocolRuns} protocol runs (seed={protocolSeed}) ===");
                SeedSecureRandom(protocolSeed);
                long totalIters = 0;
                var macroSw = Stopwatch.StartNew();
                for (int i = 0; i < protocolRuns; i++)
                {
                    var outcome = new ProtocolRunner().Run();
                    totalIters += outcome.Iterations;
                }
                macroSw.Stop();
                double sec = macroSw.Elapsed.TotalSeconds;
                Console.WriteLine($"  total elapsed     : {macroSw.Elapsed}");
                Console.WriteLine($"  per-run elapsed   : {sec / protocolRuns * 1000:N2} ms");
                Console.WriteLine($"  avg iterations    : {(double)totalIters / protocolRuns:N2}");
                Console.WriteLine($"  per-iter elapsed  : {sec / totalIters * 1000:N3} ms");
                Console.WriteLine($"  runs / sec        : {protocolRuns / sec:N3}");
            }

            return 0;
        }

        private static void SeedSecureRandom(int seed)
        {
            // Post-F1: no-op under crypto PRNG; macro wall-clock is still
            // meaningful as a throughput probe but iteration counts will vary.
            SecureRandom.Instance.SetRandomSeed(seed);
        }

        private static double Measure(string label, int durationSeconds, Func<double> work)
        {
            // warm-up
            RandomNumberGenerator.Fill(new byte[32]);
            var ops = work();
            Console.WriteLine($"  {label,-45}: {ops,15:N0} ops/s");
            return ops;
        }
    }
}
