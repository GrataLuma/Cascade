using System.Security.Cryptography;

namespace GrataCascade.Core
{
    internal static class ByteArrayUtils
    {
        /// <summary>
        /// Constant-time byte array equality. Returns true iff <paramref name="a"/>
        /// and <paramref name="b"/> have identical length AND identical content.
        /// Time complexity is independent of where the first differing byte (if any)
        /// occurs — the loop never short-circuits. Used by <see cref="HASH.Equals"/>
        /// and <see cref="Vector.Equals"/> to harden HashSet/Dictionary lookups
        /// against timing side-channels (defense-in-depth; the protocol's threat
        /// model is passive-on-transcript inside an authenticated transport, but
        /// constant-time compare costs nothing for the small byte arrays at hand
        /// and closes the door on direct-deployment timing leaks).
        ///
        /// Length check: in the GC protocol both byte arrays always come from the
        /// same fixed configuration (h_AB, h_BA, h_P, or VectorLength), so they
        /// are equal-length by construction. If the lengths differ, this returns
        /// false — same end behaviour as the previous min-prefix variant when
        /// applied to equal-length inputs, but stricter (and safer) when invariants
        /// are violated.
        /// </summary>
        public static bool FixedTimeEqual(byte[] a, byte[] b)
        {
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
