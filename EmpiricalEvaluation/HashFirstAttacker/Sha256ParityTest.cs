using System;
using System.Security.Cryptography;

namespace HashFirstAttacker
{
    /// <summary>
    /// F3.c unit-level equivalence check.
    ///
    /// Compares SHA256Managed.ComputeHash(byte[]) (the legacy managed impl used by the
    /// V2 protocol core prior to F3) against SHA256.HashData(ReadOnlySpan&lt;byte&gt;,
    /// Span&lt;byte&gt;) (the target hardware-accelerated static API). SHA-256 is
    /// specified; both implementations MUST produce byte-identical digests for every
    /// input. This test catches refactor bugs — wrong span slice, misaligned offset,
    /// forgotten buffer reuse — not cryptographic divergence.
    /// </summary>
    public static class Sha256ParityTest
    {
        public static int Run(int iterations, int? seed)
        {
            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning disable SYSLIB0021
            using var legacy = new SHA256Managed();
#pragma warning restore SYSLIB0021

            Span<byte> managedDigest;
            Span<byte> staticDigest = stackalloc byte[32];

            int maxInputLen = 4096;
            byte[] input = new byte[maxInputLen];

            int mismatches = 0;
            for (int i = 0; i < iterations; i++)
            {
                int len = rng.Next(1, maxInputLen + 1);
                rng.NextBytes(input.AsSpan(0, len));

                byte[] managedResult = legacy.ComputeHash(input, 0, len);
                managedDigest = managedResult.AsSpan();

                SHA256.HashData(input.AsSpan(0, len), staticDigest);

                if (!managedDigest.SequenceEqual(staticDigest))
                {
                    mismatches++;
                    if (mismatches <= 3)
                    {
                        Console.Error.WriteLine($"  MISMATCH #{mismatches} at iter={i} len={len}");
                        Console.Error.WriteLine($"    managed: {Convert.ToHexString(managedDigest)}");
                        Console.Error.WriteLine($"    static : {Convert.ToHexString(staticDigest)}");
                    }
                }
            }

            Console.WriteLine($"Sha256 parity test: iterations={iterations}, mismatches={mismatches}");
            return mismatches == 0 ? 0 : 1;
        }
    }
}
