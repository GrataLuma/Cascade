using System;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// Common contract for all passive attackers.
    /// An attacker receives the public Transcript and must produce a candidate K*.
    /// Return null if the attacker cannot produce a candidate (incomplete recovery).
    /// </summary>
    public abstract class AttackerBase
    {
        public abstract string Name { get; }

        public sealed class Result
        {
            public byte[] RecoveredK;
            public bool Success;              // RecoveredK matches Transcript.GroundTruthK
            public int SlotsMatched;           // how many of PasswordSlots were successfully preimaged
            public long CandidatesTried;
            public TimeSpan Elapsed;
            public string Notes;
        }

        public abstract Result AttemptRecover(Transcript t);

        protected static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
