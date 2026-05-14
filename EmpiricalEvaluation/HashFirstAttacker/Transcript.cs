using System.Collections.Generic;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// Side-channel recording of one full protocol run.
    /// Stores everything a passive eavesdropper observes on the wire — no private state.
    ///
    /// What IS recorded:
    ///   - Per round t ∈ Iterate:
    ///       * A→B: list of 4096 AtoB-length hashes (prefix of SHA256(vector))
    ///       * B→A: R vector (public random), BtoA-length hashes (up to 16), AES ciphertext
    ///   - Final round:
    ///       * Password-length hashes (4B) for the 16 password vectors
    ///
    /// What is NOT recorded (attacker cannot see):
    ///   - ClientA/B private vector pools
    ///   - SecureRandom internal state
    ///   - Derived K* (stored only for ground-truth comparison)
    /// </summary>
    public sealed class Transcript
    {
        public sealed class IterateRound
        {
            public int Tag;
            public List<HASH> AtoB_Hashes;
            public byte[] R;
            public List<HASH> BtoA_Hashes;
            public byte[] AesCiphertext;

            /// <summary>
            /// Ground-truth private leak: ClientB.seed after ProcessMessageFromA for this round.
            /// Populated by the test harness for oracle TP/FP measurement. NULL if ClientB had
            /// no match this round (passedVectors.Count == 0, seed cleared). Treat as debug-only.
            /// </summary>
            public byte[] GroundTruthSeedB;

            /// <summary>
            /// F5.e/B.6 ground-truth private leak: snapshot of the M password vectors
            /// (ClientB.passedVectors post-sort) that ClientB used to derive
            /// password = SHA256(sorted concat) for the AES encryption of the seed.
            /// Null if seed was not derived (passedVectors empty). Raw 32-byte vectors
            /// in the same sorted order used for password derivation. Test-harness only;
            /// never fed to any real attacker flow. Used by B.6 synthetic-flood attacker
            /// to construct per-slot candidate sets (1 real + K-1 fakes).
            /// </summary>
            public List<byte[]> GroundTruthPasswordVectors;
        }

        public sealed class FinalRound
        {
            public int Tag;
            public List<HASH> AtoB_Hashes;
            public List<HASH> BtoA_PasswordHashes;
        }

        public List<IterateRound> Iterate = new List<IterateRound>();
        public FinalRound Final;

        /// <summary>Only present in test harness: K* revealed after the run for ground-truth comparison.</summary>
        public byte[] GroundTruthK;

        /// <summary>Protocol parameters captured at run time (mirrors GrataCascade.Core.Configuration).</summary>
        public int VectorLength;
        public int VectorCount;
        public int HashLenAtoB;
        public int HashLenBtoA;
        public int HashLenPassword;
        public int PasswordSlots;

        public int RoundCount => Iterate.Count + (Final != null ? 1 : 0);
    }
}
