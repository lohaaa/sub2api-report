using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Services;

/// <summary>
/// 更新检查：发现固定仓库最新 Release，下载并验签 manifest，执行严格校验后与当前版本比较。
/// </summary>
public sealed class UpdateCheckService(
    IGitHubReleaseClient gitHubClient,
    IDownloader downloader,
    ReleasePublicKeyProvider publicKeyProvider,
    UpdateStateStore stateStore,
    GlobalOperationLock operationLock,
    UpdateOptions options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        // 与 deploy/build-release-assets.sh 生成的 camelCase 字段名一致；未知字段仍被拒绝。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public async Task<UpdateCheckResponse> CheckAsync(
        UpdateCheckRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentVersion);
        if (!SemanticVersion.TryParse(request.CurrentVersion, out var currentVersion)
            || currentVersion is null)
        {
            throw new UpdateOperationException(
                StatusCodes.Status400BadRequest,
                "CurrentVersion 不是有效的 SemVer。");
        }

        await using var scope = await operationLock.AcquireAsync(cancellationToken);
        try
        {
            var response = await CheckCoreAsync(request.CurrentVersion, currentVersion, cancellationToken);
            await stateStore.SaveStatusAsync(
                new UpdateStatusSnapshot(
                    LastCheckedAt: timeProvider.GetUtcNow(),
                    UpdateAvailable: response.UpdateAvailable,
                    AvailableVersion: response.AvailableVersion,
                    AvailablePublishedAt: response.PublishedAt,
                    ManualUpgradeRequired: response.ManualUpgradeRequired,
                    CurrentVersion: request.CurrentVersion,
                    LastError: null),
                cancellationToken);
            return response;
        }
        catch (UpdateOperationException exception)
        {
            await stateStore.SaveStatusAsync(
                new UpdateStatusSnapshot(
                    LastCheckedAt: timeProvider.GetUtcNow(),
                    UpdateAvailable: false,
                    AvailableVersion: null,
                    AvailablePublishedAt: null,
                    ManualUpgradeRequired: false,
                    CurrentVersion: request.CurrentVersion,
                    LastError: exception.Message),
                cancellationToken);
            throw;
        }
    }

    private async Task<UpdateCheckResponse> CheckCoreAsync(
        string currentVersionText,
        SemanticVersion currentVersion,
        CancellationToken cancellationToken)
    {
        var release = await gitHubClient.GetLatestReleaseAsync(cancellationToken);
        var manifestAsset = FindAsset(release, GitHubReleaseLocations.ManifestAssetName);
        var signatureAsset = FindAsset(release, GitHubReleaseLocations.ManifestSignatureAssetName);

        AssertAllowedAssetUrl(manifestAsset.DownloadUrl, release.TagName, GitHubReleaseLocations.ManifestAssetName);
        AssertAllowedAssetUrl(signatureAsset.DownloadUrl, release.TagName, GitHubReleaseLocations.ManifestSignatureAssetName);

        var downloadsDirectory = stateStore.GetDownloadsDirectory();
        var manifestDownload = await downloader.DownloadAsync(
            manifestAsset.DownloadUrl,
            Path.Combine(downloadsDirectory, GitHubReleaseLocations.ManifestAssetName),
            options.MaxManifestBytes,
            cancellationToken);
        var signatureDownload = await downloader.DownloadAsync(
            signatureAsset.DownloadUrl,
            Path.Combine(downloadsDirectory, GitHubReleaseLocations.ManifestSignatureAssetName),
            options.MaxManifestBytes,
            cancellationToken);

        var manifestBytes = await File.ReadAllBytesAsync(manifestDownload.FilePath, cancellationToken);
        var signatureBytes = await File.ReadAllBytesAsync(signatureDownload.FilePath, cancellationToken);
        if (!ReleaseSignatureVerifier.Verify(manifestBytes, signatureBytes, publicKeyProvider.GetPublicKey()))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "Release manifest 签名校验失败。");
        }

        ReleaseManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, ManifestSerializerOptions)
                ?? throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "Release manifest 格式无效。");
        }
        catch (JsonException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "Release manifest 格式无效。",
                exception);
        }

        var validationErrors = ReleaseManifestValidator.Validate(
            manifest,
            options.MaxDownloadBytes,
            options.MaxManifestBytes,
            timeProvider.GetUtcNow());
        if (validationErrors.Count > 0)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                $"Release manifest 未通过校验：{string.Join("；", validationErrors)}");
        }

        if (!string.Equals(release.TagName, $"v{manifest.Version}", StringComparison.Ordinal))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "Release manifest 版本与发布 Tag 不一致。");
        }

        var updateAvailable = SemanticVersion.TryParse(manifest.Version, out var available)
            && available!.CompareTo(currentVersion) > 0;

        return new UpdateCheckResponse(
            UpdateAvailable: updateAvailable,
            CurrentVersion: currentVersionText,
            AvailableVersion: manifest.Version,
            PublishedAt: manifest.PublishedAt,
            ManualUpgradeRequired: manifest.ManualUpgradeRequired);
    }

    private static GitHubReleaseAsset FindAsset(GitHubReleaseInfo release, string name)
    {
        var matches = release.Assets.Where(asset => asset.Name == name).ToList();
        if (matches.Count != 1)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                $"最新 Release 缺少必需资产 {name}。");
        }

        return matches[0];
    }

    private static void AssertAllowedAssetUrl(string url, string tag, string fileName)
    {
        if (!GitHubReleaseLocations.IsAllowedReleaseAssetUrl(url, tag, fileName))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "Release 资产地址不在固定仓库允许列表内。");
        }
    }
}
