using Microsoft.AspNetCore.DataProtection;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal sealed class ChannelSecretProtector
{
    public const string ProtectorPurpose = "Sub2ApiReport.Notifications.ChannelSecret.v1";
    private const int SuffixLength = 4;

    private readonly IDataProtector _protector;

    public ChannelSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext.Trim());

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);

    public static string CreateSuffix(string secret) => secret[^Math.Min(SuffixLength, secret.Length)..];
}
