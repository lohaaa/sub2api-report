using System.Security.Cryptography;
using System.Text;

namespace Sub2ApiReport.Updater.Security;

/// <summary>
/// 从配置的只读令牌文件加载共享令牌（Compose Updater__TokenFile）。文件内容必须是 64 位十六进制字符。
/// 加载失败或内容无效时一律拒绝（fail closed）；结果在首次加载后缓存，绝不记录令牌内容。
/// </summary>
public sealed class UpdaterTokenProvider(string tokenFilePath)
{
    private const int RequiredHexLength = 64;
    private readonly object _lock = new();
    private byte[]? _cachedToken;
    private bool _loadAttempted;

    public bool Matches(string providedToken)
    {
        var expected = GetToken();
        if (expected is null)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        return providedBytes.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expected);
    }

    private byte[]? GetToken()
    {
        if (_loadAttempted)
        {
            return _cachedToken;
        }

        lock (_lock)
        {
            if (_loadAttempted)
            {
                return _cachedToken;
            }

            _loadAttempted = true;
            _cachedToken = Load();
            return _cachedToken;
        }
    }

    private byte[]? Load()
    {
        string content;
        try
        {
            content = File.ReadAllText(tokenFilePath).Trim();
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException)
        {
            return null;
        }

        if (content.Length != RequiredHexLength || !content.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return Encoding.UTF8.GetBytes(content);
    }
}
