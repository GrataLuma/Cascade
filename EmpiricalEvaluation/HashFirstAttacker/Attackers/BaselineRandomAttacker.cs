using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;

namespace HashFirstAttacker.Attackers
{
    /// <summary>
    /// B.1 baseline: sample random 32-byte vectors, SHA-256, compare the password-hash
    /// slice (bytes [HashLenAtoB + HashLenBtoA .. +HashLenPassword)) against the Final
    /// hashes from the transcript.
    ///
    /// Per task B.1, this is a calibration benchmark — full recovery is not expected
    /// within a 30-minute budget. Reports progress and extrapolates the wall time to
    /// complete recovery (coupon-collector estimate 2^(8·L) · H_s / rate).
    /// </summary>
    public sealed class BaselineRandomAttacker : AttackerBase
    {
        public override string Name => "B.1 Baseline Random Sampling";

        public TimeSpan Budget { get; set; } = TimeSpan.FromMinutes(30);
        public long MaxSamples { get; set; } = long.MaxValue;
        public Action<long, int, TimeSpan> ProgressCallback { get; set; }
        public long ProgressInterval { get; set; } = 2_000_000;

        public override Result AttemptRecover(Transcript t)
        {
            int slotCount = t.Final.BtoA_PasswordHashes.Count;
            int pwLen = t.HashLenPassword;
            int offsetPw = t.HashLenAtoB + t.HashLenBtoA;
            int vectorLen = t.VectorLength;
            if (pwLen < 1 || pwLen > 8) throw new NotSupportedException($"pwLen={pwLen} unsupported (need 1..8)");

            var slotByHash = new Dictionary<ulong, int>();
            for (int i = 0; i < slotCount; i++)
            {
                ulong key = HashKey(t.Final.BtoA_PasswordHashes[i].Data, 0, pwLen);
                if (!slotByHash.ContainsKey(key)) slotByHash[key] = i;
            }

            var found = new byte[slotCount][];
            int slotsFilled = 0;
            long samples = 0;
            TimeSpan firstHit = TimeSpan.Zero;

            var rng = RandomNumberGenerator.Create();
            byte[] v = new byte[vectorLen];
            byte[] h = new byte[32];

            var sw = Stopwatch.StartNew();
            long nextProgress = ProgressInterval;

            while (slotsFilled < slotCount && sw.Elapsed < Budget && samples < MaxSamples)
            {
                rng.GetBytes(v);
                SHA256.HashData(v, h);
                ulong key = HashKey(h, offsetPw, pwLen);

                if (slotByHash.TryGetValue(key, out int slot) && found[slot] == null)
                {
                    found[slot] = (byte[])v.Clone();
                    slotsFilled++;
                    if (firstHit == TimeSpan.Zero) firstHit = sw.Elapsed;
                }

                samples++;
                if (samples >= nextProgress)
                {
                    ProgressCallback?.Invoke(samples, slotsFilled, sw.Elapsed);
                    nextProgress += ProgressInterval;
                }
            }
            sw.Stop();

            byte[] recoveredK = null;
            if (slotsFilled == slotCount)
            {
                var sorted = new List<byte[]>(found);
                sorted.Sort(LexCompare);
                var concat = new byte[sorted.Count * vectorLen];
                for (int i = 0; i < sorted.Count; i++) Buffer.BlockCopy(sorted[i], 0, concat, i * vectorLen, vectorLen);
                recoveredK = SHA256.HashData(concat);
            }

            double seconds = sw.Elapsed.TotalSeconds;
            double rate = seconds > 0 ? samples / seconds : 0;
            double etaSeconds = EstimateFullEta(slotCount, rate, pwLen);

            string notes = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "budget={0}, rate={1:F0}/s, firstHit={2}, slots={3}/{4}, ETA_full≈{5}",
                HashFirstAttacker.Cli.TimeFormat.Format(Budget), rate, slotsFilled > 0 ? HashFirstAttacker.Cli.TimeFormat.Format(firstHit) : "n/a",
                slotsFilled, slotCount, HashFirstAttacker.Cli.TimeFormat.Format(TimeSpan.FromSeconds(Math.Min(etaSeconds, TimeSpan.MaxValue.TotalSeconds))));

            return new Result
            {
                RecoveredK = recoveredK,
                Success = recoveredK != null && BytesEqual(recoveredK, t.GroundTruthK),
                SlotsMatched = slotsFilled,
                CandidatesTried = samples,
                Elapsed = sw.Elapsed,
                Notes = notes
            };
        }

        private static ulong HashKey(byte[] buf, int offset, int length)
        {
            ulong k = 0;
            for (int i = 0; i < length; i++) k = (k << 8) | buf[offset + i];
            return k;
        }

        private static int LexCompare(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] < b[i]) return -1;
                if (a[i] > b[i]) return 1;
            }
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>Coupon-collector ETA: 2^(8L) · H_s samples to cover all s slots.</summary>
        private static double EstimateFullEta(int slotCount, double rate, int pwLen)
        {
            if (rate <= 0 || slotCount <= 0) return double.PositiveInfinity;
            double h = 0; for (int k = 1; k <= slotCount; k++) h += 1.0 / k;
            double expected = Math.Pow(2, 8 * pwLen) * h;
            return expected / rate;
        }

        // Format(TimeSpan) moved to HashFirstAttacker.Cli.TimeFormat in R1 refactor.
    }
}
