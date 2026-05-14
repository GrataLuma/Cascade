using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    /// <summary>
    /// Party B in the Grata Cascade protocol. Owns a vector pool, runs the
    /// hash-match-then-AES key derivation each round, and emits messages for
    /// ClientA. F-task contributions accumulated in this class:
    /// <list type="bullet">
    ///   <item>F4 — CandidateMax filter (drop over-typical password candidates)
    ///         and CutLimit pool-refill safety; diagnostics: RoundsSkippedByF4Filter,
    ///         FillRejections, FillSafetyTriggers.</item>
    ///   <item>F5.e / B.6 — LastPassedVectorsSnapshot and LastPassedProbabilities
    ///         expose the post-Top-M passwordVector set for the synthetic-flood
    ///         indistinguishability test (test-harness only).</item>
    ///   <item>F8 — AES zero-pad on encrypt path (EncryptSeedWithF8Padding)
    ///         for VectorLength &lt; 16.</item>
    /// </list>
    /// </summary>
    public class ClientB
    {
        public HashSet<Vector> vectors;

        public byte[] seed;

        public List<Stat> stats;

        public List<Vector> passwordVectors;

        /// <summary>
        /// F4 diagnostic: count of rounds where the CandidateMax filter reduced
        /// passedVectors below AESPassedVectorsCount and the round was skipped.
        /// Distinct from rounds where passedVectors was already 0 pre-filter.
        /// </summary>
        public int RoundsSkippedByF4Filter;

        /// <summary>F4 diagnostic: cumulative count of pool-vector candidates rejected
        /// by the CutLimit filter across all FillVectors calls this run.</summary>
        public int FillRejections;

        /// <summary>F4 diagnostic: cumulative count of vectors that hit the
        /// CutLimitSafetyAttempts cap and were accepted unfiltered.</summary>
        public int FillSafetyTriggers;

        /// <summary>
        /// F5.e/B.6 ground-truth accessor: snapshot of the M password vectors
        /// (passedVectors post-sort) used in the most recent ProcessMessageFromA
        /// call to derive password = SHA256(sorted concat). Null if the round
        /// produced no seed (filter reject, empty passedVectors). Test-harness only;
        /// captured by <see cref="HashFirstAttacker.ProtocolRunner"/> after each call
        /// and stored in <c>Transcript.IterateRound.GroundTruthPasswordVectors</c>.
        /// </summary>
        public List<byte[]> LastPassedVectorsSnapshot;
        public List<double> LastPassedProbabilities;

        public ClientB()
        {
            vectors = new HashSet<Vector>();
            stats = new List<Stat>();
            passwordVectors = new List<Vector>();

            FillVectors(0);
        }

        private void FillVectors(int tag)
        {
            int safetyBreached = 0;
            bool cutLimitActive = !double.IsNegativeInfinity(Configuration.CutLimitProbabilityLog2);

            if (seed == null)
            {
                Stat stat = new Stat(Configuration.VectorLength, "R" + tag.ToString());
                stats.Add(stat);

                while (vectors.Count < Configuration.VectorCount)
                {
                    Vector candidate = new Vector(Configuration.VectorLength, stat);
                    if (cutLimitActive)
                    {
                        int attempts = 1;
                        while (stat.GetProbabilityLog(candidate) >= Configuration.CutLimitProbabilityLog2)
                        {
                            if (attempts >= Configuration.CutLimitSafetyAttempts) { safetyBreached++; FillSafetyTriggers++; break; }
                            FillRejections++;
                            candidate = new Vector(Configuration.VectorLength, stat);
                            attempts++;
                        }
                    }
                    vectors.Add(candidate);
                }
            }
            else {

                Stat stat = new Stat(Configuration.VectorLength, "S" + tag.ToString());
                stats.Add(stat);

                while (vectors.Count < Configuration.VectorCount)
                {
                    Vector candidate = new Vector(Configuration.VectorLength, seed, stat);
                    if (cutLimitActive)
                    {
                        int attempts = 1;
                        while (stat.GetProbabilityLog(candidate) >= Configuration.CutLimitProbabilityLog2)
                        {
                            if (attempts >= Configuration.CutLimitSafetyAttempts) { safetyBreached++; FillSafetyTriggers++; break; }
                            FillRejections++;
                            candidate = new Vector(Configuration.VectorLength, seed, stat);
                            attempts++;
                        }
                    }
                    vectors.Add(candidate);
                }
            }

            if (safetyBreached > 0)
            {
                Console.Error.WriteLine($"[F4 ClientB.FillVectors tag={tag}] WARN: {safetyBreached} vector(s) hit CutLimit safety cap (threshold log2={Configuration.CutLimitProbabilityLog2})");
            }
        }

        /// <summary>
        /// F8.b fix: AES-CBC with PaddingMode.None requires block-aligned input.
        /// For <see cref="Configuration.VectorLength"/> &lt; 16 (e.g. L=4, L=8) the
        /// seed is padded to the next 16-byte multiple before encryption. Padding
        /// bytes are drawn from <see cref="SecureRandom"/> (NOT zero-filled) — zero
        /// padding leaks a distinguisher: a brute-force attacker can identify the
        /// correct password by checking trailing zeros in the decrypted plaintext
        /// (wrong key → pseudo-random output, right key → trailing zeros). Random
        /// padding hides the seed-length boundary completely.
        ///
        /// Decrypt side (<see cref="SeedProvider"/>) truncates back to VectorLength,
        /// discarding the random tail. For L &gt;= 16 with L%16==0 (the 16, 32, 64
        /// production shapes) this is a no-op and the wire format is unchanged.
        ///
        /// The mirror fix on the message-buffer init side lives in
        /// <see cref="MessageToClientA"/> (commit d946aa5; covers the
        /// passedVectors==0 fallback path where no encryption happens at all).
        /// </summary>
        private static byte[] EncryptSeedWithF8Padding(byte[] seed, byte[] password)
        {
            const int aesBlock = 16;
            int paddedLen = ((Configuration.VectorLength + aesBlock - 1) / aesBlock) * aesBlock;
            byte[] toEncrypt = seed;
            if (paddedLen != Configuration.VectorLength)
            {
                toEncrypt = new byte[paddedLen];
                Array.Copy(seed, toEncrypt, Configuration.VectorLength);
                // F8.b: random tail padding instead of zero-fill — see XML doc above.
                int tailLen = paddedLen - Configuration.VectorLength;
                byte[] tail = new byte[tailLen];
                SecureRandom.Instance.FillBuffer(tail);
                Array.Copy(tail, 0, toEncrypt, Configuration.VectorLength, tailLen);
            }
            return AES.Encrypt(toEncrypt, password, null);
        }

        public MessageToClientA ProcessMessageFromA(MessageToClientB messageFromA, int tag) {

            HashSet<HASH> hashesFromA = messageFromA.V.ToHashSet();

            List<Vector> freeVectors = new List<Vector>();
            List<Vector> passedVectors = new List<Vector>();

            //same?
            foreach (Vector vector in vectors) {

                if (hashesFromA.Contains(vector.ComputeHash(0, Configuration.HASHLength_AtoB)))
                {
                    passedVectors.Add(vector.Copy());
                }
                else {

                    freeVectors.Add(vector);
                }
            }

            //reduce
            MessageToClientA export = new MessageToClientA();

            //filter
            foreach (Stat stat in stats) stat.UpdateWithRandomVector(export.R);

            vectors.Clear();

            foreach (Vector vector in freeVectors) {

                if (SecureRandom.Instance.NextDouble() > Configuration.NoUpdateLimit)
                {
                    vector.UpdateWithVector(export.R);
                }
                else {

                    vector.NotUpdateCount++;
                }

                vectors.Add(vector);
            }

            //AES generate
            if (passedVectors.Count > 0)
            {
                passedVectors.Sort((x,y) => x.Probability.CompareTo(y.Probability));

                // F4 (b): drop over-typical collision vectors before top-M selection.
                // Only candidates whose Stat-log2-probability is more negative than
                // the threshold survive. If fewer than AESPassedVectorsCount survive,
                // the round is skipped (passedVectors cleared → seed=null branch below).
                // When the filter is disabled (threshold = -Infinity), pre-F4 semantics
                // hold: any passedVectors.Count > 0 proceeds with AES + password.
                if (!double.IsNegativeInfinity(Configuration.CandidateMaxProbabilityLog2))
                {
                    int preFilterCount = passedVectors.Count;
                    passedVectors = passedVectors.Where(v => v.Probability < Configuration.CandidateMaxProbabilityLog2).ToList();
                    if (passedVectors.Count < Configuration.AESPassedVectorsCount)
                    {
                        if (preFilterCount >= Configuration.AESPassedVectorsCount)
                        {
                            // Only counted when the round WOULD have proceeded pre-F4 but F4 killed it.
                            RoundsSkippedByF4Filter++;
                        }
                        passedVectors.Clear();
                    }
                }
            }

            if (passedVectors.Count > 0)
            {
                if(passedVectors.Count > Configuration.AESPassedVectorsCount) passedVectors = passedVectors.GetRange(0, Configuration.AESPassedVectorsCount);

                //password candidates
                foreach (Vector vector in passedVectors)
                {
                    if (vector.Tag.Contains("S")) passwordVectors.Add(vector);
                    break;
                }

                passedVectors.Sort();

                // F5.e/B.6 ground-truth snapshot: clone raw byte data of each
                // post-sort passedVector. The same sorted order is used below for
                // password derivation, so the snapshot is order-consistent.
                LastPassedVectorsSnapshot = new List<byte[]>(passedVectors.Count);
                LastPassedProbabilities = new List<double>(passedVectors.Count);
                foreach (var pv in passedVectors)
                {
                    var clone = new byte[pv.Data.Length];
                    Buffer.BlockCopy(pv.Data, 0, clone, 0, pv.Data.Length);
                    LastPassedVectorsSnapshot.Add(clone);
                    LastPassedProbabilities.Add(pv.Probability);
                }

                //generate password and hashes
                List<byte> temp = new List<byte>();

                for (int i = 0; i < passedVectors.Count; i++) {

                    temp.AddRange(passedVectors[i].Data);
                    export.hashes.Add(passedVectors[i].ComputeHash(Configuration.HASHLength_AtoB, Configuration.HASHLength_BtoA));
                }

                byte[] password = SHA256.HashData(temp.ToArray());

                seed = new byte[Configuration.VectorLength];
                SecureRandom.Instance.FillBuffer(seed);
                export.message = EncryptSeedWithF8Padding(seed, password);

            }
            else {

                seed = null;
                LastPassedVectorsSnapshot = null;
                LastPassedProbabilities = null;
            }

            //fake hashes
            while (export.hashes.Count < Configuration.AESPassedVectorsCount) {

                export.hashes.Add(new HASH());
            }

            //password complete?

            if (passwordVectors.Count > 0) {

                passwordVectors.Sort((x,y) => x.Probability.CompareTo(y.Probability));

                double prop = 0;

                foreach (Vector v in passwordVectors) {

                    prop += v.Probability;
                } 

                if (prop < Configuration.TargetProbability)
                {
                    export.Tag = "Final";
                    export.hashes.Clear();
                    export.message = null;
                    export.R = null;

                    foreach (Vector vector in passwordVectors)
                    {
                        export.hashes.Add(vector.ComputeHash(Configuration.HASHLength_AtoB + Configuration.HASHLength_BtoA, Configuration.HASHLength_Password));

                        Console.WriteLine(vector.ToString());
                    }
                }
            }

            //add new vectors
            FillVectors(tag);

            return export;
        }

        public byte[] GetPassword()
        {
            if (passwordVectors == null) return new byte[0];

            passwordVectors.Sort();

            List<byte> temp = new List<byte>();
            passwordVectors.ForEach(x => temp.AddRange(x.Data));

            return SHA256.HashData(temp.ToArray());
        }

        public double GetProbability()
        {
            double export = 0;

            foreach (Vector vector in passwordVectors)
            {
                export += vector.Probability;
            }

            return export;
        }
    }
}
