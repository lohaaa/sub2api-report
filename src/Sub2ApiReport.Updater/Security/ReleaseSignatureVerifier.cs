using System.Security.Cryptography;

namespace Sub2ApiReport.Updater.Security;

/// <summary>
/// Release manifest 签名校验：RSA SHA-256 PKCS#1 v1.5。
/// </summary>
public static class ReleaseSignatureVerifier
{
    internal const int MaximumSignatureBytes = 8192;

    public static bool Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature, RSAParameters publicKey)
    {
        if (signature.IsEmpty || signature.Length > MaximumSignatureBytes)
        {
            return false;
        }

        using var rsa = RSA.Create(publicKey);
        return rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
