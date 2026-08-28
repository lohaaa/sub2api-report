using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Releases;

/// <summary>与 deploy/build-release-assets.sh 生成的 camelCase 字段名一致的 manifest 反序列化选项。</summary>
public static class ReleaseManifestJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static ReleaseManifest? TryDeserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<ReleaseManifest>(bytes, SerializerOptions);
}

/// <summary>持久化的最近一次已验签 Release（完整 manifest + 签名），供安装事务复用。</summary>
public sealed record ReleaseCacheEntry(
    DateTimeOffset CachedAt,
    string ManifestJson,
    string SignatureBase64);

/// <summary>经验证签名的缓存 Release。</summary>
public sealed record CachedRelease(DateTimeOffset CachedAt, ReleaseManifest Manifest);

public interface IReleaseCacheService
{
    Task SaveAsync(ReleaseManifest manifest, byte[] manifestBytes, byte[] signatureBytes, CancellationToken cancellationToken);

    /// <summary>加载缓存 Release 并重新验签与校验；缺失或不合法返回 null。</summary>
    Task<CachedRelease?> LoadVerifiedAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 验签 Release 缓存：安装事务只信任缓存中通过签名校验与结构校验的 manifest，
/// 不接受任何请求提供的 URL 或镜像信息。
/// </summary>
public sealed class ReleaseCacheService(
    UpdateStateStore stateStore,
    ReleasePublicKeyProvider publicKeyProvider,
    UpdateOptions options,
    TimeProvider timeProvider) : IReleaseCacheService
{
    public async Task SaveAsync(
        ReleaseManifest manifest,
        byte[] manifestBytes,
        byte[] signatureBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await stateStore.SaveReleaseCacheAsync(
            new ReleaseCacheEntry(
                timeProvider.GetUtcNow(),
                Encoding.UTF8.GetString(manifestBytes),
                Convert.ToBase64String(signatureBytes)),
            cancellationToken);
    }

    public async Task<CachedRelease?> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        var entry = await stateStore.LoadReleaseCacheAsync(cancellationToken);
        if (entry is null)
        {
            return null;
        }

        byte[] manifestBytes;
        byte[] signatureBytes;
        try
        {
            // 签名针对原始 manifest 字节，必须原样恢复后再验签。
            manifestBytes = Encoding.UTF8.GetBytes(entry.ManifestJson);
            signatureBytes = Convert.FromBase64String(entry.SignatureBase64);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return null;
        }

        ReleaseManifest? manifest;
        try
        {
            manifest = ReleaseManifestJson.TryDeserialize(manifestBytes);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null)
        {
            return null;
        }

        if (!ReleaseSignatureVerifier.Verify(manifestBytes, signatureBytes, publicKeyProvider.GetPublicKey()))
        {
            return null;
        }

        var validationErrors = ReleaseManifestValidator.Validate(
            manifest,
            options.MaxDownloadBytes,
            options.MaxManifestBytes,
            timeProvider.GetUtcNow());
        if (validationErrors.Count > 0)
        {
            return null;
        }

        return new CachedRelease(entry.CachedAt, manifest);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await stateStore.SaveReleaseCacheAsync(
            new ReleaseCacheEntry(default, string.Empty, string.Empty),
            cancellationToken);
    }
}
