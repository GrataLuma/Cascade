using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class Vector : IComparable
    {
        public byte[] Data { get; set; }

        public Stat statRef;

        public double Probability;
        public int OneCount;
        public string Tag;
        public int NotUpdateCount;

        public int PassCount;

        private Vector(byte[] data) { 
        
            Data = new byte[data.Length];
            Array.Copy(data, Data, data.Length);
        }

        public Vector(int initialLength, Stat statRef) {

            Data = new byte[initialLength];
            this.statRef = statRef;

            SecureRandom.Instance.FillBuffer(Data);
        }

        public Vector(int initialLength, byte[] seed, Stat statRef) {

            Data = new byte[initialLength];
            this.statRef = statRef;

            for (int i = 0; i < initialLength; i++) {

                Data[i] = (byte)Math.Max(0, Math.Min(255, seed[i] + SecureRandom.Instance.Next(-Configuration.SeedMinMax, Configuration.SeedMinMax + 1)));
            }
        }

        public Vector Copy() { 
        
            Vector export = new Vector(Data);
            export.Probability = statRef.GetProbabilityLog(this);
            export.OneCount = statRef.GetOneCount(this);
            export.Tag = statRef.Tag;
            export.NotUpdateCount = NotUpdateCount;
            return export;

        }

        public void UpdateWithVector(byte[] randomVector)
        {
            for (int i = 0; i < Data.Length; i++)
            {
                int temp = Data[i] + randomVector[i] - 128;

                if (temp > 255 || temp < 0)
                {
                    Data[i] = randomVector[i];
                }
                else
                {
                    Data[i] = (byte)temp;
                }
            }
        }

        public HASH ComputeHash(int offset, int length) {

            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(Data, digest);
            return new HASH(digest, offset, length);
        }

        public override int GetHashCode()
        {
            if (Data.Length >= 4) return Data[0] | Data[1] << 8 | Data[2] << 16 | Data[3] << 24;
            if (Data.Length == 3) return Data[0] | Data[1] << 8 | Data[2] << 16;
            if (Data.Length == 2) return Data[0] | Data[1] << 8;

            return Data[0];
        }

        public override bool Equals(object obj)
        {
            Vector test = obj as Vector;

            if (test == null) return false;

            return CompareTwoVectors(this, test);
        }

        public int CompareTo(object obj)
        {
            Vector test = obj as Vector;

            for (int i = 0; i < Math.Min(test.Data.Length, Data.Length); i++) {

                if (Data[i] > test.Data[i]) return 1;
                if (Data[i] < test.Data[i]) return -1;
            }

            return 0;
        }

        public override string ToString()
        {
            return "Prob: " + Probability + "; "  + BitConverter.ToString(Data) + "; " + "Tag: " + Tag + "; NoUpdateCount: " + NotUpdateCount;
        }

        public static bool CompareTwoVectors(Vector a, Vector b)
        {
            return ByteArrayUtils.FixedTimeEqual(a.Data, b.Data);
        }
    }
}
