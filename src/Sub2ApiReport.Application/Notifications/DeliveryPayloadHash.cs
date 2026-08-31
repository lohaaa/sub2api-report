using System.Security.Cryptography;
using System.Text;

namespace Sub2ApiReport.Application.Notifications;

/// <summary>Computes deterministic payload hashes for delivery records.</summary>
public static class DeliveryPayloadHash
{
    public static string Compute(string subject, string body, byte[]? attachmentContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));
        ArgumentNullException.ThrowIfNull(body, nameof(body));

        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(incrementalHash, subject);
        AppendSeparator(incrementalHash);
        Append(incrementalHash, body);
        AppendSeparator(incrementalHash);
        incrementalHash.AppendData(attachmentContent ?? []);
        return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
    }

    private static void AppendSeparator(IncrementalHash hash) => hash.AppendData("\n\n"u8);
}
