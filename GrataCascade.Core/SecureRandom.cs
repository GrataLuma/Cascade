using System;
using System.Security.Cryptography;

namespace GrataCascade.Core
{
    /// <summary>
    /// Cryptographically secure PRNG wrapper (F1 migration).
    ///
    /// All four public methods delegate to the static <see cref="RandomNumberGenerator"/>
    /// surface (.NET built-in) which dispatches to the OS CSPRNG (BCryptGenRandom on
    /// Windows, /dev/urandom on Linux). All members are thread-safe by virtue of the
    /// underlying static API — the singleton carries no mutable state.
    ///
    /// Pre-F1 this class wrapped <see cref="System.Random"/> (non-cryptographic 48-bit
    /// LCG seeded from Environment.TickCount). Empirical artifacts produced before F1
    /// are invalidated by this migration and must be regenerated (see
    /// reports/rng_migration_notes.md).
    /// </summary>
    public class SecureRandom
    {
        private static SecureRandom instance;
        private static bool seedWarningEmitted;

        public static SecureRandom Instance
        {
            get {

                if (instance == null) instance = new SecureRandom();

                return instance;
            }
        }

        private SecureRandom() {
        }

        /// <summary>Returns a non-negative Int32 uniformly distributed in [0, int.MaxValue].</summary>
        public int Next()
        {
            Span<byte> buf = stackalloc byte[4];
            RandomNumberGenerator.Fill(buf);
            int v = BitConverter.ToInt32(buf);
            return v & 0x7FFFFFFF;
        }

        /// <summary>Fills the buffer with cryptographically secure random bytes.</summary>
        public void FillBuffer(byte[] buffer)
        {
            RandomNumberGenerator.Fill(buffer);
        }

        /// <summary>
        /// Uniformly random Int32 in [min, max). Uses .NET's built-in unbiased
        /// rejection sampling via <see cref="RandomNumberGenerator.GetInt32(int, int)"/>.
        /// </summary>
        public int Next(int min, int max)
        {
            return RandomNumberGenerator.GetInt32(min, max);
        }

        /// <summary>
        /// Uniformly random double in [0, 1) with full 53-bit precision.
        /// Uses the top 53 bits of a 64-bit uniform draw so every representable
        /// double in the interval is equally likely (no modulo bias).
        /// </summary>
        public double NextDouble()
        {
            Span<byte> buf = stackalloc byte[8];
            RandomNumberGenerator.Fill(buf);
            ulong u = BitConverter.ToUInt64(buf);
            return (u >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>
        /// Post-F1 NO-OP retained for callers that still pass a seed for CLI
        /// compatibility. Cryptographic RNG is unpredictable by design and cannot be
        /// seeded; determinism-dependent workflows (e.g. the F3 verify-sha
        /// record/verify bit-exact check) no longer reproduce across runs.
        /// </summary>
        public void SetRandomSeed(int seed)
        {
            if (!seedWarningEmitted)
            {
                seedWarningEmitted = true;
                Console.Error.WriteLine(
                    "[SecureRandom] WARN: SetRandomSeed is a no-op under the F1 crypto PRNG. " +
                    "Determinism-dependent workflows (verify-sha --mode verify, seeded NIST key banks) " +
                    "will not reproduce. See reports/rng_migration_notes.md.");
            }
        }
    }
}
