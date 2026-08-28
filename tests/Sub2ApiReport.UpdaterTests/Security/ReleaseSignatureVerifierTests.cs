using System.Security.Cryptography;
using Sub2ApiReport.Updater.Security;

namespace Sub2ApiReport.UpdaterTests.Security;

public sealed class ReleaseSignatureVerifierTests
{
    private static readonly byte[] Manifest = "release-manifest-bytes"u8.ToArray();

    [Fact]
    public void ValidSignatureVerifies()
    {
        var (key, _) = TestKeys.CreateSigningKey();
        using (key)
        {
            var signature = TestKeys.Sign(key, Manifest);

            Assert.True(ReleaseSignatureVerifier.Verify(Manifest, signature, key.ExportParameters(false)));
        }
    }

    [Fact]
    public void TamperedManifestFailsVerification()
    {
        var (key, _) = TestKeys.CreateSigningKey();
        using (key)
        {
            var signature = TestKeys.Sign(key, Manifest);
            var tamperedManifest = Manifest.ToArray();
            tamperedManifest[0] ^= 0x01;

            Assert.False(ReleaseSignatureVerifier.Verify(tamperedManifest, signature, key.ExportParameters(false)));
        }
    }

    [Fact]
    public void TamperedSignatureFailsVerification()
    {
        var (key, _) = TestKeys.CreateSigningKey();
        using (key)
        {
            var signature = TestKeys.Sign(key, Manifest);
            var tampered = signature.ToArray();
            tampered[^1] ^= 0xFF;

            Assert.False(ReleaseSignatureVerifier.Verify(Manifest, tampered, key.ExportParameters(false)));
        }
    }

    [Fact]
    public void SignatureFromDifferentKeyFailsVerification()
    {
        var (signingKey, _) = TestKeys.CreateSigningKey();
        var (otherKey, _) = TestKeys.CreateSigningKey();
        using (signingKey)
        using (otherKey)
        {
            var signature = TestKeys.Sign(signingKey, Manifest);

            Assert.False(ReleaseSignatureVerifier.Verify(Manifest, signature, otherKey.ExportParameters(false)));
        }
    }

    [Fact]
    public void EmptySignatureIsRejectedWithoutVerification()
    {
        var (key, _) = TestKeys.CreateSigningKey();
        using (key)
        {
            Assert.False(ReleaseSignatureVerifier.Verify(Manifest, [], key.ExportParameters(false)));
        }
    }

    [Fact]
    public void OversizedSignatureIsRejectedWithoutVerification()
    {
        var (key, _) = TestKeys.CreateSigningKey();
        using (key)
        {
            Assert.False(ReleaseSignatureVerifier.Verify(
                Manifest,
                new byte[ReleaseSignatureVerifier.MaximumSignatureBytes + 1],
                key.ExportParameters(false)));
        }
    }
}
