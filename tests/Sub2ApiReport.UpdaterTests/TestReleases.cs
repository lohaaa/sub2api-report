using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests;

internal static class TestReleases
{
    public const string DefaultVersion = "1.2.0";
    public const string CurrentAppVersion = "0.7.0";

    public static readonly DateTimeOffset PublishedAt = new(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);

    public static string Hex(char character, int length = 64) => new(character, length);

    public static string ManifestAssetUrl(string version) =>
        $"https://github.com/lohaaa/sub2api-report/releases/download/v{version}/release-manifest.json";

    public static string ManifestSignatureUrl(string version) =>
        $"https://github.com/lohaaa/sub2api-report/releases/download/v{version}/release-manifest.sig";

    public static string AppArchiveUrl(string version) =>
        $"https://github.com/lohaaa/sub2api-report/releases/download/v{version}/sub2api-report-app-v{version}-linux-amd64.tar.gz";

    public static string UpdaterArchiveUrl(string version) =>
        $"https://github.com/lohaaa/sub2api-report/releases/download/v{version}/sub2api-report-updater-v{version}-linux-amd64.tar.gz";

    public static GitHubReleaseInfo CreateRelease(
        string version,
        byte[] manifestBytes,
        byte[] signatureBytes) => new(
        $"v{version}",
        PublishedAt,
        [
            new GitHubReleaseAsset("release-manifest.json", ManifestAssetUrl(version), manifestBytes.Length),
            new GitHubReleaseAsset("release-manifest.sig", ManifestSignatureUrl(version), signatureBytes.Length),
        ]);

    /// <summary>与发布脚本一致：camelCase 字段名。</summary>
    public static byte[] ToJson(ReleaseManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal static class TestKeys
{
    public static (RSA Key, string PublicPem) CreateSigningKey()
    {
        var rsa = RSA.Create(2048);
        return (rsa, rsa.ExportSubjectPublicKeyInfoPem());
    }

    public static byte[] Sign(RSA key, byte[] payload) =>
        key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"sub2api-updater-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

internal sealed class ReleaseManifestBuilder
{
    private string _version = TestReleases.DefaultVersion;
    private string _channel = "stable";
    private DateTimeOffset _publishedAt = TestReleases.PublishedAt;
    private string _architecture = "linux/amd64";
    private int _schemaVersion = UpdateContractConstants.ManifestSchemaVersion;
    private int _deploymentContractVersion = UpdateContractConstants.DeploymentContractVersion;
    private string _minimumUpdaterVersion = TestReleases.DefaultVersion;
    private bool _manualUpgradeRequired = true;
    private bool _onlineInstallSupported;
    private string _signatureAlgorithm = UpdateContractConstants.SignatureAlgorithm;
    private string _appArchiveUrl = TestReleases.AppArchiveUrl(TestReleases.DefaultVersion);
    private string _appArchiveSha256 = TestReleases.Hex('a');
    private string _appImageId = "sha256:" + TestReleases.Hex('b');
    private string _appLoadedTag = UpdateContractConstants.AppLoadedTagPrefix + TestReleases.DefaultVersion;
    private long _appSize = 1024;
    private string _updaterArchiveUrl = TestReleases.UpdaterArchiveUrl(TestReleases.DefaultVersion);
    private string _updaterArchiveSha256 = TestReleases.Hex('c');
    private string _updaterImageId = "sha256:" + TestReleases.Hex('d');
    private string _updaterLoadedTag = UpdateContractConstants.UpdaterLoadedTagPrefix + TestReleases.DefaultVersion;
    private long _updaterSize = 512;
    private bool _selfUpdateSupported;
    private string _targetMigration = "20260826000000_ExampleMigration";
    private string _releaseNotesPageUrl = GitHubReleaseLocations.GetReleasePageUrl(TestReleases.DefaultVersion);
    private string _releaseNotesAssetUrl =
        $"https://github.com/lohaaa/sub2api-report/releases/download/v{TestReleases.DefaultVersion}/release-notes-v{TestReleases.DefaultVersion}.md";
    private string _releaseNotesSha256 = TestReleases.Hex('e');
    private long _releaseNotesSize = 2048;

    public ReleaseManifest Build() => new(
        _schemaVersion,
        _version,
        _channel,
        _publishedAt,
        _architecture,
        _deploymentContractVersion,
        _minimumUpdaterVersion,
        _manualUpgradeRequired,
        _onlineInstallSupported,
        _signatureAlgorithm,
        new ReleaseAppArtifact(_appArchiveUrl, _appArchiveSha256, _appImageId, _appLoadedTag, _appSize),
        new ReleaseUpdaterArtifact(
            _updaterArchiveUrl,
            _updaterArchiveSha256,
            _updaterImageId,
            _updaterLoadedTag,
            _updaterSize,
            _selfUpdateSupported),
        new ReleaseDatabaseSection(_targetMigration, RequiresBackupRestoreForRollback: true),
        new ReleaseNotesSection(_releaseNotesPageUrl, _releaseNotesAssetUrl, _releaseNotesSha256, _releaseNotesSize));

    public ReleaseManifestBuilder WithVersion(string version)
    {
        _version = version;
        _minimumUpdaterVersion = version;
        _appArchiveUrl = TestReleases.AppArchiveUrl(version);
        _appLoadedTag = UpdateContractConstants.AppLoadedTagPrefix + version;
        _updaterArchiveUrl = TestReleases.UpdaterArchiveUrl(version);
        _updaterLoadedTag = UpdateContractConstants.UpdaterLoadedTagPrefix + version;
        _releaseNotesPageUrl = GitHubReleaseLocations.GetReleasePageUrl(version);
        _releaseNotesAssetUrl =
            $"https://github.com/lohaaa/sub2api-report/releases/download/v{version}/release-notes-v{version}.md";
        return this;
    }

    public ReleaseManifestBuilder WithSchemaVersion(int value)
    {
        _schemaVersion = value;
        return this;
    }

    public ReleaseManifestBuilder WithChannel(string value)
    {
        _channel = value;
        return this;
    }

    public ReleaseManifestBuilder WithArchitecture(string value)
    {
        _architecture = value;
        return this;
    }

    public ReleaseManifestBuilder WithDeploymentContractVersion(int value)
    {
        _deploymentContractVersion = value;
        return this;
    }

    public ReleaseManifestBuilder WithMinimumUpdaterVersion(string value)
    {
        _minimumUpdaterVersion = value;
        return this;
    }

    public ReleaseManifestBuilder WithSignatureAlgorithm(string value)
    {
        _signatureAlgorithm = value;
        return this;
    }

    public ReleaseManifestBuilder WithPublishedAt(DateTimeOffset value)
    {
        _publishedAt = value;
        return this;
    }

    public ReleaseManifestBuilder WithOnlineInstallSupported(bool value)
    {
        _onlineInstallSupported = value;
        return this;
    }

    public ReleaseManifestBuilder WithAppArchiveUrl(string value)
    {
        _appArchiveUrl = value;
        return this;
    }

    public ReleaseManifestBuilder WithAppArchiveSha256(string value)
    {
        _appArchiveSha256 = value;
        return this;
    }

    public ReleaseManifestBuilder WithAppImageId(string value)
    {
        _appImageId = value;
        return this;
    }

    public ReleaseManifestBuilder WithAppLoadedTag(string value)
    {
        _appLoadedTag = value;
        return this;
    }

    public ReleaseManifestBuilder WithAppSize(long value)
    {
        _appSize = value;
        return this;
    }

    public ReleaseManifestBuilder WithUpdaterArchiveUrl(string value)
    {
        _updaterArchiveUrl = value;
        return this;
    }

    public ReleaseManifestBuilder WithUpdaterArchiveSha256(string value)
    {
        _updaterArchiveSha256 = value;
        return this;
    }

    public ReleaseManifestBuilder WithUpdaterImageId(string value)
    {
        _updaterImageId = value;
        return this;
    }

    public ReleaseManifestBuilder WithUpdaterLoadedTag(string value)
    {
        _updaterLoadedTag = value;
        return this;
    }

    public ReleaseManifestBuilder WithUpdaterSize(long value)
    {
        _updaterSize = value;
        return this;
    }

    public ReleaseManifestBuilder WithSelfUpdateSupported(bool value)
    {
        _selfUpdateSupported = value;
        return this;
    }

    public ReleaseManifestBuilder WithTargetMigration(string value)
    {
        _targetMigration = value;
        return this;
    }

    public ReleaseManifestBuilder WithReleaseNotesPageUrl(string value)
    {
        _releaseNotesPageUrl = value;
        return this;
    }

    public ReleaseManifestBuilder WithReleaseNotesAssetUrl(string value)
    {
        _releaseNotesAssetUrl = value;
        return this;
    }

    public ReleaseManifestBuilder WithReleaseNotesSha256(string value)
    {
        _releaseNotesSha256 = value;
        return this;
    }

    public ReleaseManifestBuilder WithReleaseNotesSize(long value)
    {
        _releaseNotesSize = value;
        return this;
    }
}
