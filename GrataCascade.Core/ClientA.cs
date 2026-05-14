using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    /// <summary>
    /// Party A in the Grata Cascade protocol. Maintains a vector pool with full
    /// hash-keyed history (allVectors) and the persistent passwordVectors used
    /// for K* derivation post-Final. F-task contributions in this class:
    /// <list type="bullet">
    ///   <item>F4 — CutLimit pool-refill diagnostics (FillRejections,
    ///         FillSafetyTriggers).</item>
    ///   <item>F5.e — passwordVectors persisted across rounds for the eventual
    ///         K* check at termination.</item>
    /// </list>
    /// Note: <c>allVectors</c> grows unboundedly per run (per-round add, never
    /// cleared) — see grata_cascade_protocol_quirks memory note.
    /// </summary>
    public class ClientA
    {
        public HashSet<Vector> vectors;

        public List<Stat> stats;

        public List<byte[]> seeds = new List<byte[]>();

        public Dictionary<HASH, List<Vector>> allVectors;

        public List<Vector> passwordVectors;

        /// <summary>F4 diagnostic: cumulative count of pool-vector candidates rejected
        /// by the CutLimit filter across all FillVectors calls this run.</summary>
        public int FillRejections;

        /// <summary>F4 diagnostic: cumulative count of vectors that hit the
        /// CutLimitSafetyAttempts cap and were accepted unfiltered.</summary>
        public int FillSafetyTriggers;

        public ClientA() {

            vectors = new HashSet<Vector>();
            stats = new List<Stat>();
            allVectors = new Dictionary<HASH, List<Vector>>();

            FillVectors(0);
        }

        private void FillVectors(int tag) {

            int skip = vectors.Count;
            int safetyBreached = 0;
            bool cutLimitActive = !double.IsNegativeInfinity(Configuration.CutLimitProbabilityLog2);

            if (seeds.Count == 0)
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
                    Vector candidate = new Vector(Configuration.VectorLength, seeds[SecureRandom.Instance.Next() % seeds.Count], stat);
                    if (cutLimitActive)
                    {
                        int attempts = 1;
                        while (stat.GetProbabilityLog(candidate) >= Configuration.CutLimitProbabilityLog2)
                        {
                            if (attempts >= Configuration.CutLimitSafetyAttempts) { safetyBreached++; FillSafetyTriggers++; break; }
                            FillRejections++;
                            candidate = new Vector(Configuration.VectorLength, seeds[SecureRandom.Instance.Next() % seeds.Count], stat);
                            attempts++;
                        }
                    }
                    vectors.Add(candidate);
                }
            }

            if (safetyBreached > 0)
            {
                Console.Error.WriteLine($"[F4 ClientA.FillVectors tag={tag}] WARN: {safetyBreached} vector(s) hit CutLimit safety cap (threshold log2={Configuration.CutLimitProbabilityLog2})");
            }
        }

        public MessageToClientB CreateMessageToB() {

            MessageToClientB export = new MessageToClientB();

            export.V = new List<HASH>();

            foreach (Vector vector in vectors) {

                export.V.Add(vector.ComputeHash(0, Configuration.HASHLength_AtoB));
                HASH a = vector.ComputeHash(Configuration.HASHLength_AtoB + Configuration.HASHLength_BtoA, Configuration.HASHLength_Password);

                //save to allVectors

                if (allVectors.ContainsKey(a))
                {
                    allVectors[a].Add(vector.Copy());
                }
                else {

                    allVectors.Add(a, new List<Vector> { vector.Copy() });
                }
            }

            return export;
        }

        public void ProcessMessageFromB(MessageToClientA messageFromB, int tag) {

            if (messageFromB.Tag.Contains("Final")) {

                passwordVectors = new List<Vector>();
                HashSet<HASH> hashesFromB = messageFromB.hashes.ToHashSet();

                foreach (HASH a in hashesFromB) {

                    if (allVectors.ContainsKey(a)) {

                        passwordVectors.Add(allVectors[a][0]);
                    }
                }
            }

            if (messageFromB.Tag.Contains("Iterate")) {

                HashSet<HASH> hashesFromB = messageFromB.hashes.ToHashSet();

                Dictionary<HASH, List<Vector>> dict = new Dictionary<HASH, List<Vector>>();

                foreach (Vector vector in vectors)
                {
                    HASH a = vector.ComputeHash(Configuration.HASHLength_AtoB, Configuration.HASHLength_BtoA);

                    if (hashesFromB.Contains(a))
                    {
                        if (dict.ContainsKey(a))
                        {
                            dict[a].Add(vector);
                        }
                        else
                        {

                            List<Vector> list = new List<Vector>();
                            list.Add(vector);

                            dict.Add(a, list);
                        }
                    }
                }

                if (dict.Count > 0)
                {
                    seeds = SeedProvider.GetAllSeeds(dict, messageFromB.message);
                }
                else
                {

                    seeds.Clear();
                }

                //filter
                foreach (Stat stat in stats) stat.UpdateWithRandomVector(messageFromB.R);

                List<Vector> freeVectors = new List<Vector>(vectors);
                vectors.Clear();

                foreach (Vector vector in freeVectors)
                {
                    if (SecureRandom.Instance.NextDouble() > Configuration.NoUpdateLimit) {

                        vector.UpdateWithVector(messageFromB.R);
                    }
                    else
                    {
                        vector.NotUpdateCount++;
                    }

                    vectors.Add(vector);
                }

                FillVectors(tag);
            }
        }

        public byte[] GetPassword() {

            if (passwordVectors == null) return new byte[0];

            passwordVectors.Sort();

            List<byte> temp = new List<byte>();
            passwordVectors.ForEach(x => temp.AddRange(x.Data));

            return SHA256.HashData(temp.ToArray());
        }

        public double GetProbability() {

            double export = 0;

            foreach(Vector vector in passwordVectors)
            {
                export += vector.Probability;
            }

            return export;
        }
    }
}
