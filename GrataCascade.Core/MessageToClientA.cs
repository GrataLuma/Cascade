using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    /// <summary>
    /// Round message sent from B to A. Carries the public random vector R, B's
    /// hash prefixes for the round (h_BA over passwordVectors when present;
    /// otherwise random padding), the AES-encrypted seed (or null at Final),
    /// and the round tag (Iterate or Final).
    ///
    /// F-task contributions:
    /// <list type="bullet">
    ///   <item>F8 (constructor coverage gap fix, commit d946aa5) — the
    ///         <c>message</c> field's pre-init random buffer is padded to a
    ///         16-byte AES block boundary so the no-encrypt fallback path
    ///         (passedVectors==0) doesn't break AES.Decrypt for L &lt; 16.
    ///         Mirrors <see cref="ClientB"/>'s encrypt-side F8 fix.</item>
    /// </list>
    /// </summary>
    public class MessageToClientA
    {
        public byte[] R;
        public List<HASH> hashes;
        public byte[] message;
        public string Tag;

        public MessageToClientA() {

            R = new byte[Configuration.VectorLength];
            SecureRandom.Instance.FillBuffer(R);

            // F8 fix coverage: when ClientB skips encryption (passedVectors.Count == 0),
            // this constructor's random init survives unchanged on the wire. ClientA's
            // SeedProvider then calls AES.Decrypt(message) which requires block-aligned
            // input. ClientB.ProcessMessageFromA pads encrypt input up to 16; mirror
            // that here so the unencrypted-fallback path has a valid AES block size too.
            // For VectorLength >= 16 with L % 16 == 0 (16, 32, 64), msgLen == VectorLength
            // (no behavioural change). For L < 16 (4, 8), pad up to 16.
            int aesBlock = 16;
            int msgLen = ((Configuration.VectorLength + aesBlock - 1) / aesBlock) * aesBlock;
            message = new byte[msgLen];
            SecureRandom.Instance.FillBuffer(message);

            hashes = new List<HASH>();

            Tag = "Iterate";
        }
    }
}
