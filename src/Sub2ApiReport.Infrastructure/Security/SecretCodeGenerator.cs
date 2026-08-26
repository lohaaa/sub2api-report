using System.Security.Cryptography;
using System.Text;

namespace Sub2ApiReport.Infrastructure.Security;

internal static class SecretCodeGenerator
{
    private const int EntropyBytes = 16;

    public static GeneratedSecretCode Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(EntropyBytes);
        var hexadecimal = Convert.ToHexString(bytes);
        var code = string.Join(
            '-',
            Enumerable.Range(0, hexadecimal.Length / 4)
                .Select(index => hexadecimal.Substring(index * 4, 4)));

        return new GeneratedSecretCode(code, Hash(code));
    }

    public static bool Verify(string suppliedCode, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        var suppliedHash = Hash(suppliedCode);
        return expectedHash.Length == suppliedHash.Length
            && CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static byte[] Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = new string(code
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }
}

internal sealed record GeneratedSecretCode(string Code, byte[] Hash);
