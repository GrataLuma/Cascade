using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class HASH
    {
        public byte[] Data;

        public Vector parentVector;

        public HASH() { 
        
            Data = new byte[Configuration.HASHLength_BtoA];
            SecureRandom.Instance.FillBuffer(Data);
        }

        public HASH(byte[] data, int offset, int length) {

            Data = new byte[length];

            Array.Copy(data, offset, Data, 0, length);
        }

        public HASH(ReadOnlySpan<byte> digest, int offset, int length) {

            Data = digest.Slice(offset, length).ToArray();
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
            HASH test = obj as HASH;

            if (test == null) return false;

            return CompareTwoVectors(this, test);
        }

        public static bool CompareTwoVectors(HASH a, HASH b)
        {
            return ByteArrayUtils.FixedTimeEqual(a.Data, b.Data);
        }
    }
}
