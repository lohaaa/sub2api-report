using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Updater.Releases;

/// <summary>
/// 对已通过签名校验的 manifest 执行严格结构校验，字段与 deploy/build-release-assets.sh 生成的
/// release-manifest.json 一一对应。任何规则失败都会拒绝本次 Release。
/// </summary>
public static class ReleaseManifestValidator
{
    public static IReadOnlyList<string> Validate(
        ReleaseManifest manifest,
        long maxDownloadBytes,
        long maxMetadataBytes,
        DateTimeOffset currentTime)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();

        if (manifest.SchemaVersion != UpdateContractConstants.ManifestSchemaVersion)
        {
            errors.Add("manifest schemaVersion 不受支持。");
        }

        if (!string.Equals(manifest.Channel, UpdateContractConstants.StableChannel, StringComparison.Ordinal))
        {
            errors.Add("manifest channel 仅支持 stable。");
        }

        if (!SemanticVersion.TryParse(manifest.Version, out var version) || version!.HasPrerelease)
        {
            errors.Add("manifest version 必须是不带预发布标识的 SemVer。");
        }


        if (!string.Equals(manifest.Architecture, UpdateContractConstants.Architecture, StringComparison.Ordinal))
        {
            errors.Add("manifest architecture 仅支持 linux/amd64。");
        }

        if (manifest.DeploymentContractVersion != UpdateContractConstants.DeploymentContractVersion)
        {
            errors.Add("manifest deploymentContractVersion 不受支持。");
        }

        if (!string.Equals(
                manifest.SignatureAlgorithm,
                UpdateContractConstants.SignatureAlgorithm,
                StringComparison.Ordinal))
        {
            errors.Add("manifest signatureAlgorithm 不受支持。");
        }

        if (manifest.PublishedAt == default
            || manifest.PublishedAt > currentTime + TimeSpan.FromHours(1))
        {
            errors.Add("manifest publishedAt 无效。");
        }

        ValidateCompatibility(manifest, version, errors);
        ValidateAppArtifact(manifest, version, maxDownloadBytes, errors);
        ValidateUpdaterArtifact(manifest, version, maxDownloadBytes, errors);
        ValidateDatabaseSection(manifest, errors);
        ValidateReleaseNotes(manifest, version, maxMetadataBytes, errors);

        return errors;
    }

    private static void ValidateCompatibility(
        ReleaseManifest manifest,
        SemanticVersion? targetVersion,
        List<string> errors)
    {
        if (!SemanticVersion.TryParse(manifest.MinimumUpdaterVersion, out var minimumUpdaterVersion)
            || minimumUpdaterVersion is null
            || minimumUpdaterVersion.HasPrerelease)
        {
            errors.Add("manifest minimumUpdaterVersion 不是有效的稳定 SemVer。");
        }
        else if (targetVersion is not null && minimumUpdaterVersion.CompareTo(targetVersion) > 0)
        {
            errors.Add("manifest minimumUpdaterVersion 不能高于目标版本。");
        }

        if (string.IsNullOrWhiteSpace(manifest.UpgradeMessage) || manifest.UpgradeMessage.Length > 300)
        {
            errors.Add("manifest upgradeMessage 长度必须为 1–300。");
        }

        var sources = manifest.OnlineUpgradeFrom;
        if (sources is null)
        {
            errors.Add("manifest onlineUpgradeFrom 不能为空。");
            return;
        }

        if (manifest.ManualUpgradeRequired == manifest.OnlineInstallSupported
            || (manifest.OnlineInstallSupported && sources.Count == 0)
            || (manifest.ManualUpgradeRequired && sources.Count != 0))
        {
            errors.Add("manifest 在线安装策略与 onlineUpgradeFrom 不一致。");
        }

        if (sources.Count != sources.Distinct(StringComparer.Ordinal).Count())
        {
            errors.Add("manifest onlineUpgradeFrom 不能包含重复版本。");
        }

        foreach (var source in sources)
        {
            if (!SemanticVersion.TryParse(source, out var sourceVersion)
                || sourceVersion is null
                || sourceVersion.HasPrerelease
                || (targetVersion is not null && sourceVersion.CompareTo(targetVersion) >= 0))
            {
                errors.Add("manifest onlineUpgradeFrom 只能包含低于目标版本的稳定 SemVer。");
                break;
            }
        }
    }

    private static void ValidateAppArtifact(
        ReleaseManifest manifest,
        SemanticVersion? version,
        long maxDownloadBytes,
        List<string> errors)
    {
        var app = manifest.App;
        if (app.Size <= 0 || app.Size > maxDownloadBytes)
        {
            errors.Add("app.size 超出允许范围。");
        }

        if (!IsSha256Hex(app.ArchiveSha256))
        {
            errors.Add("app.archiveSha256 不是有效的 SHA-256 摘要。");
        }

        if (!IsValidImageId(app.ImageId))
        {
            errors.Add("app.imageId 无效。");
        }

        if (!IsValidImageId(app.TargetDigest))
        {
            errors.Add("app.targetDigest 无效。");
        }

        if (version is not null)
        {
            var expectedTag = $"v{manifest.Version}";
            var expectedFileName = GitHubReleaseLocations.GetAppArchiveFileName(manifest.Version);
            if (!GitHubReleaseLocations.IsAllowedReleaseAssetUrl(app.ArchiveUrl, expectedTag, expectedFileName))
            {
                errors.Add("app.archiveUrl 不在固定 Release 下载路径内。");
            }

            var expectedLoadedTag = $"{UpdateContractConstants.AppLoadedTagPrefix}{manifest.Version}";
            if (!string.Equals(app.LoadedTag, expectedLoadedTag, StringComparison.Ordinal))
            {
                errors.Add("app.loadedTag 与版本不一致。");
            }
        }
    }

    private static void ValidateUpdaterArtifact(
        ReleaseManifest manifest,
        SemanticVersion? version,
        long maxDownloadBytes,
        List<string> errors)
    {
        var updater = manifest.Updater;
        if (updater.SelfUpdateSupported)
        {
            errors.Add("updater.selfUpdateSupported 必须为 false。");
        }

        if (updater.Size <= 0 || updater.Size > maxDownloadBytes)
        {
            errors.Add("updater.size 超出允许范围。");
        }

        if (!IsSha256Hex(updater.ArchiveSha256))
        {
            errors.Add("updater.archiveSha256 不是有效的 SHA-256 摘要。");
        }

        if (!IsValidImageId(updater.ImageId))
        {
            errors.Add("updater.imageId 无效。");
        }

        if (!IsValidImageId(updater.TargetDigest))
        {
            errors.Add("updater.targetDigest 无效。");
        }

        if (version is not null)
        {
            var expectedTag = $"v{manifest.Version}";
            var expectedFileName = GitHubReleaseLocations.GetUpdaterArchiveFileName(manifest.Version);
            if (!GitHubReleaseLocations.IsAllowedReleaseAssetUrl(updater.ArchiveUrl, expectedTag, expectedFileName))
            {
                errors.Add("updater.archiveUrl 不在固定 Release 下载路径内。");
            }

            var expectedLoadedTag = $"{UpdateContractConstants.UpdaterLoadedTagPrefix}{manifest.Version}";
            if (!string.Equals(updater.LoadedTag, expectedLoadedTag, StringComparison.Ordinal))
            {
                errors.Add("updater.loadedTag 与版本不一致。");
            }
        }
    }

    private static void ValidateDatabaseSection(ReleaseManifest manifest, List<string> errors)
    {
        if (!IsValidTargetMigration(manifest.Database.TargetMigration))
        {
            errors.Add("database.targetMigration 无效。");
        }
    }

    private static void ValidateReleaseNotes(
        ReleaseManifest manifest,
        SemanticVersion? version,
        long maxMetadataBytes,
        List<string> errors)
    {
        var releaseNotes = manifest.ReleaseNotes;
        if (releaseNotes.Size <= 0 || releaseNotes.Size > maxMetadataBytes)
        {
            errors.Add("releaseNotes.size 超出允许范围。");
        }

        if (!IsSha256Hex(releaseNotes.Sha256))
        {
            errors.Add("releaseNotes.sha256 不是有效的 SHA-256 摘要。");
        }

        if (version is null)
        {
            return;
        }

        var expectedPageUrl = GitHubReleaseLocations.GetReleasePageUrl(manifest.Version);
        if (!string.Equals(releaseNotes.PageUrl, expectedPageUrl, StringComparison.Ordinal))
        {
            errors.Add("releaseNotes.pageUrl 不在固定 Release 页面路径内。");
        }

        var expectedAssetFileName = GitHubReleaseLocations.GetReleaseNotesFileName(manifest.Version);
        if (!GitHubReleaseLocations.IsAllowedReleaseAssetUrl(
                releaseNotes.AssetUrl,
                $"v{manifest.Version}",
                expectedAssetFileName))
        {
            errors.Add("releaseNotes.assetUrl 不在固定 Release 下载路径内。");
        }
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    private static bool IsValidImageId(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        return value["sha256:".Length..].All(char.IsAsciiHexDigit);
    }

    private static bool IsValidTargetMigration(string value)
    {
        if (value.Length < 16 || value.Length > 200)
        {
            return false;
        }

        if (!value.AsSpan(0, 14)[..].ToArray().All(char.IsAsciiDigit) || value[14] != '_')
        {
            return false;
        }

        return value[15..].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
