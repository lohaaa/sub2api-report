using System.Security.Cryptography;

namespace Sub2ApiReport.Updater.Security;

/// <summary>
/// 从配置的只读公钥文件加载发布签名公钥。公钥路径只能来自配置，不能来自请求或 Release 内容。
/// </summary>
public sealed class ReleasePublicKeyProvider(string publicKeyPath)
{
    private readonly object _lock = new();
    private RSAParameters? _cachedParameters;

    public RSAParameters GetPublicKey()
    {
        if (_cachedParameters is { } cached)
        {
            return cached;
        }

        lock (_lock)
        {
            if (_cachedParameters is { } cachedParameters)
            {
                return cachedParameters;
            }

            var parameters = Load();
            _cachedParameters = parameters;
            return parameters;
        }
    }

    private RSAParameters Load()
    {
        string pem;
        try
        {
            pem = File.ReadAllText(publicKeyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UpdateOperationException(
                StatusCodes.Status500InternalServerError,
                "无法读取配置的发布公钥文件。",
                exception);
        }

        if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateOperationException(
                StatusCodes.Status500InternalServerError,
                "发布公钥文件不允许包含私钥。");
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new UpdateOperationException(
                StatusCodes.Status500InternalServerError,
                "发布公钥文件格式无效。",
                exception);
        }

        if (rsa.KeySize < 2048)
        {
            throw new UpdateOperationException(
                StatusCodes.Status500InternalServerError,
                "发布公钥长度不足。");
        }

        return rsa.ExportParameters(includePrivateParameters: false);
    }
}
