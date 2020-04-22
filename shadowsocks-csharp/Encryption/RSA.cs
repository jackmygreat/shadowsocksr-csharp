using System.Security.Cryptography;

namespace Shadowsocks.Encryption
{
    internal class RSA
    {
        public static bool SignatureVerify(string p_strKeyPublic, byte[] rgb, byte[] rgbSignature)
        {
            try
            {
                var key = new RSACryptoServiceProvider();
                key.FromXmlString(p_strKeyPublic);
                var deformatter = new RSAPKCS1SignatureDeformatter(key);
                deformatter.SetHashAlgorithm("SHA512");
                if (deformatter.VerifySignature(rgb, rgbSignature)) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}