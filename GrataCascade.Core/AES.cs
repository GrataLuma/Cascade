using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace GrataCascade.Core
{
    public class AES
    {
        private const int ITERATIONS = 10;
        private static readonly byte[] DefaultSalt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        public static byte[] Encrypt(byte[] bytesToBeEncrypted, byte[] key, byte[] salt)
        {
            return Transform(bytesToBeEncrypted, key, salt, encrypt: true);
        }

        public static byte[] Decrypt(byte[] bytesToBeDecrypted, byte[] key, byte[] salt)
        {
            return Transform(bytesToBeDecrypted, key, salt, encrypt: false);
        }

        private static byte[] Transform(byte[] data, byte[] key, byte[] salt, bool encrypt)
        {
            if (salt == null) salt = DefaultSalt;

            // PBKDF2-HMAC-SHA1 (Rfc2898DeriveBytes default) je managed v .NET — funguje
            // ve WASM. Generuje 48 bytes (32 B key + 16 B IV) v jednom streamu, identicky
            // s původním AesManaged code path: derivedKey.GetBytes(32) + GetBytes(16).
            byte[] derivedKey, iv;
            using (var pbkdf2 = new Rfc2898DeriveBytes(key, salt, ITERATIONS, HashAlgorithmName.SHA1))
            {
                derivedKey = pbkdf2.GetBytes(32);
                iv = pbkdf2.GetBytes(16);
            }

            // BouncyCastle AES-CBC + NoPadding. Vstup MUSÍ být násobek 16 — F8 fix
            // zajišťuje pre-padding na block boundary v MessageToClientA constructor.
            var cipher = new BufferedBlockCipher(new CbcBlockCipher(new AesEngine()));
            var keyParam = new KeyParameter(derivedKey);
            var ivParam = new ParametersWithIV(keyParam, iv);
            cipher.Init(encrypt, ivParam);

            byte[] output = new byte[cipher.GetOutputSize(data.Length)];
            int len = cipher.ProcessBytes(data, 0, data.Length, output, 0);
            len += cipher.DoFinal(output, len);

            if (len == output.Length) return output;
            byte[] trimmed = new byte[len];
            Buffer.BlockCopy(output, 0, trimmed, 0, len);
            return trimmed;
        }
    }
}
