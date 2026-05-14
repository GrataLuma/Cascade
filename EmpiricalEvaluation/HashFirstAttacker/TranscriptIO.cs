using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using GrataCascade.Core;

namespace HashFirstAttacker
{
    /// <summary>
    /// Binary serialization of Transcript collections for N=300+ evaluation.
    /// Header: magic(4) + version(4) + count(4) + 6×params(4 each) = 40 bytes.
    /// Each transcript record prefixed with int32 length, followed by the record bytes.
    /// Companion .idx file holds int64 offsets for O(1) random access.
    /// </summary>
    public static class TranscriptIO
    {
        public const int Magic = 0x54525043; // "TRPC"
        public const int Version = 1;

        public static void GenerateAndSave(
            int count,
            string binPath,
            string idxPath,
            Action<int, int, TimeSpan> progress = null,
            int maxAttempts = 0)
        {
            EnsureDir(binPath);
            if (maxAttempts == 0) maxAttempts = count * 3;

            using var bin = File.Create(binPath);
            using var idx = File.Create(idxPath);
            using var bw = new BinaryWriter(bin);
            using var iw = new BinaryWriter(idx);

            int vl = Configuration.VectorLength;
            int vc = Configuration.VectorCount;
            int hA = Configuration.HASHLength_AtoB;
            int hB = Configuration.HASHLength_BtoA;
            int hP = Configuration.HASHLength_Password;
            int slots = Configuration.AESPassedVectorsCount;

            bw.Write(Magic);
            bw.Write(Version);
            bw.Write(count);
            bw.Write(vl); bw.Write(vc); bw.Write(hA); bw.Write(hB); bw.Write(hP); bw.Write(slots);

            iw.Write(count);

            var runner = new ProtocolRunner();
            int produced = 0, attempts = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (produced < count && attempts < maxAttempts)
            {
                attempts++;
                var outcome = runner.Run();
                if (!outcome.FinalHit || !outcome.KeysMatch) continue;

                long offset = bin.Position;
                iw.Write(offset);
                WriteTranscript(bw, outcome.Transcript);
                produced++;

                progress?.Invoke(produced, count, sw.Elapsed);
            }

            if (produced < count)
                throw new InvalidOperationException($"Could not produce {count} valid transcripts; got {produced} in {attempts} attempts.");
        }

        private static void WriteTranscript(BinaryWriter bw, Transcript t)
        {
            long startPos = bw.BaseStream.Position;
            bw.Write(0); // placeholder for length

            bw.Write(t.Iterate.Count);
            foreach (var r in t.Iterate)
            {
                bw.Write(r.Tag);
                WriteHashList(bw, r.AtoB_Hashes);
                WriteBytes(bw, r.R);
                WriteHashList(bw, r.BtoA_Hashes);
                WriteBytes(bw, r.AesCiphertext);
                WriteBytes(bw, r.GroundTruthSeedB);
            }
            bw.Write(t.Final.Tag);
            WriteHashList(bw, t.Final.AtoB_Hashes);
            WriteHashList(bw, t.Final.BtoA_PasswordHashes);
            WriteBytes(bw, t.GroundTruthK);

            long endPos = bw.BaseStream.Position;
            int recordLen = (int)(endPos - startPos - 4);
            bw.BaseStream.Position = startPos;
            bw.Write(recordLen);
            bw.BaseStream.Position = endPos;
        }

        private static void WriteHashList(BinaryWriter bw, List<HASH> list)
        {
            if (list == null) { bw.Write(0); bw.Write(0); return; }
            bw.Write(list.Count);
            int len = list.Count > 0 ? list[0].Data.Length : 0;
            bw.Write(len);
            for (int i = 0; i < list.Count; i++) bw.Write(list[i].Data, 0, len);
        }

        private static void WriteBytes(BinaryWriter bw, byte[] b)
        {
            if (b == null) { bw.Write(0); return; }
            bw.Write(b.Length);
            bw.Write(b, 0, b.Length);
        }

        public static TranscriptReader OpenReader(string binPath, string idxPath)
            => new TranscriptReader(binPath, idxPath);

        public static string ComputeFileHashHex(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Thread-safe random-access reader. Caller creates one per thread (avoids locking
    /// around a single FileStream). Offsets loaded once into memory from .idx.
    /// </summary>
    public sealed class TranscriptReader : IDisposable
    {
        private readonly string _binPath;
        private readonly long[] _offsets;
        private readonly int _vl, _vc, _hA, _hB, _hP, _slots;
        private readonly FileStream _fs;
        private readonly BinaryReader _br;
        private readonly object _lock = new object();

        public int Count => _offsets.Length;
        public int VectorLength => _vl;
        public int VectorCount => _vc;
        public int HashLenAtoB => _hA;
        public int HashLenBtoA => _hB;
        public int HashLenPassword => _hP;
        public int PasswordSlots => _slots;

        public TranscriptReader(string binPath, string idxPath)
        {
            _binPath = binPath;
            _fs = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _br = new BinaryReader(_fs);

            int magic = _br.ReadInt32();
            if (magic != TranscriptIO.Magic) throw new InvalidDataException("Bad magic");
            int version = _br.ReadInt32();
            if (version != TranscriptIO.Version) throw new InvalidDataException($"Unsupported version {version}");
            int count = _br.ReadInt32();
            _vl = _br.ReadInt32(); _vc = _br.ReadInt32(); _hA = _br.ReadInt32();
            _hB = _br.ReadInt32(); _hP = _br.ReadInt32(); _slots = _br.ReadInt32();

            using (var idxFs = File.OpenRead(idxPath))
            using (var idxBr = new BinaryReader(idxFs))
            {
                int idxCount = idxBr.ReadInt32();
                if (idxCount != count) throw new InvalidDataException("Index count mismatch");
                _offsets = new long[count];
                for (int i = 0; i < count; i++) _offsets[i] = idxBr.ReadInt64();
            }
        }

        public Transcript Load(int index)
        {
            if (index < 0 || index >= _offsets.Length) throw new ArgumentOutOfRangeException(nameof(index));
            lock (_lock)
            {
                _fs.Position = _offsets[index];
                int recordLen = _br.ReadInt32();
                var t = new Transcript
                {
                    VectorLength = _vl,
                    VectorCount = _vc,
                    HashLenAtoB = _hA,
                    HashLenBtoA = _hB,
                    HashLenPassword = _hP,
                    PasswordSlots = _slots
                };
                int iterCount = _br.ReadInt32();
                for (int i = 0; i < iterCount; i++)
                {
                    var r = new Transcript.IterateRound
                    {
                        Tag = _br.ReadInt32(),
                        AtoB_Hashes = ReadHashList(),
                        R = ReadBytes(),
                        BtoA_Hashes = ReadHashList(),
                        AesCiphertext = ReadBytes(),
                        GroundTruthSeedB = ReadBytes()
                    };
                    t.Iterate.Add(r);
                }
                t.Final = new Transcript.FinalRound
                {
                    Tag = _br.ReadInt32(),
                    AtoB_Hashes = ReadHashList(),
                    BtoA_PasswordHashes = ReadHashList()
                };
                t.GroundTruthK = ReadBytes();
                return t;
            }
        }

        private List<HASH> ReadHashList()
        {
            int n = _br.ReadInt32();
            int len = _br.ReadInt32();
            var list = new List<HASH>(n);
            for (int i = 0; i < n; i++)
            {
                byte[] buf = new byte[len];
                _br.Read(buf, 0, len);
                list.Add(new HASH(buf, 0, len));
            }
            return list;
        }

        private byte[] ReadBytes()
        {
            int n = _br.ReadInt32();
            if (n == 0) return null;
            byte[] buf = new byte[n];
            _br.Read(buf, 0, n);
            return buf;
        }

        public void Dispose()
        {
            _br?.Dispose();
            _fs?.Dispose();
        }
    }
}
