using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace GrataCascade.Core
{
    public class SeedProvider
    {
        public static List<byte[]> GetAllSeeds(Dictionary<HASH, List<Vector>> dict, byte[] message) { 
        
            //sort items by count
            List<HASH> hashes = dict.Keys.ToList();
            hashes = hashes.OrderBy(hash => dict[hash].Count).ToList();

            Vector[] workArray = new Vector[hashes.Count];

            List<byte[]> export = new List<byte[]>();

            recursive(0, hashes, dict, workArray, export, message);

            return export;
        }

        private static void recursive(int index, List<HASH> hashes, Dictionary<HASH, List<Vector>> dict,  Vector[] workArray, List<byte[]> export, byte[] message) {

            for (int i = 0; i < dict[hashes[index]].Count; i++) {

                workArray[index] = dict[hashes[index]][i];

                if (index < hashes.Count - 1)
                {
                    recursive(index + 1, hashes, dict, workArray, export, message);
                }
                else { 
                
                    List<Vector> passedVectors = workArray.ToList();
                    passedVectors.Sort();

                    //generate password and hashes
                    List<byte> temp = new List<byte>();
                    for (int a = 0; a < passedVectors.Count; a++)
                    {
                        temp.AddRange(passedVectors[a].Data);
                    }

                    byte[] password = SHA256.HashData(temp.ToArray());

                    // F8.b: ClientB pads sub-block-size seeds (L < 16) up to the next
                    // multiple of 16 before encryption with random tail bytes (see
                    // ClientB.ProcessMessageFromA / EncryptSeedWithF8Padding).
                    // Truncate back to VectorLength so downstream sees the original seed shape.
                    byte[] decrypted = AES.Decrypt(message, password, null);
                    if (decrypted.Length > Configuration.VectorLength)
                    {
                        byte[] trimmed = new byte[Configuration.VectorLength];
                        System.Array.Copy(decrypted, trimmed, Configuration.VectorLength);
                        decrypted = trimmed;
                    }
                    export.Add(decrypted);
                }
            }
        }
    }
}
