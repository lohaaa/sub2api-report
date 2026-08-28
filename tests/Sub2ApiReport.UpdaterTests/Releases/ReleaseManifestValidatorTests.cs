using System.Text.Json;
using System.Text.Json.Nodes;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests.Releases;

public sealed class ReleaseManifestValidatorTests
{
    private static readonly DateTimeOffset Now = TestReleases.PublishedAt;
    private const long MaxDownloadBytes = 1_073_741_824;
    private const long MaxMetadataBytes = 1024 * 1024;

    [Fact]
    public void ValidManifestPassesAllRules()
    {
        AssertRejected(new ReleaseManifestBuilder().Build(), string.Empty, expectEmpty: true);
    }

    [Fact]
    public void RejectsUnsupportedSchemaVersion() =>
        AssertRejected(new ReleaseManifestBuilder().WithSchemaVersion(2).Build(), "schemaVersion");

    [Fact]
    public void RejectsNonStableChannel() =>
        AssertRejected(new ReleaseManifestBuilder().WithChannel("beta").Build(), "channel");

    [Fact]
    public void RejectsUnsupportedArchitecture() =>
        AssertRejected(new ReleaseManifestBuilder().WithArchitecture("linux/arm64").Build(), "architecture");

    [Fact]
    public void RejectsUnsupportedDeploymentContractVersion() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithDeploymentContractVersion(2).Build(),
            "deploymentContractVersion");

    [Fact]
    public void RejectsUnsupportedSignatureAlgorithm() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithSignatureAlgorithm("ECDSA-P256-SHA256").Build(),
            "signatureAlgorithm");

    [Fact]
    public void RejectsPrereleaseVersionOnStableChannel() =>
        AssertRejected(new ReleaseManifestBuilder().WithVersion("1.2.0-rc.1").Build(), "version");

    [Fact]
    public void RejectsMalformedVersion()
    {
        var manifest = new ReleaseManifestBuilder().Build();
        var invalid = JsonNode.Parse(JsonSerializer.Serialize(manifest))!;
        invalid["Version"] = "1.2";
        AssertRejected(JsonSerializer.Deserialize<ReleaseManifest>(invalid.ToJsonString())!, "version");
    }

    [Fact]
    public void RejectsMalformedMinimumUpdaterVersion() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithMinimumUpdaterVersion("1.2").Build(),
            "minimumUpdaterVersion");

    [Fact]
    public void RejectsMissingPublishedAt() =>
        AssertRejected(new ReleaseManifestBuilder().WithPublishedAt(default).Build(), "publishedAt");

    [Fact]
    public void RejectsPublishedAtTooFarInFuture() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithPublishedAt(Now + TimeSpan.FromHours(2)).Build(),
            "publishedAt");

    [Fact]
    public void RejectsAppSizeZero() =>
        AssertRejected(new ReleaseManifestBuilder().WithAppSize(0).Build(), "app.size");

    [Fact]
    public void RejectsAppSizeOverLimit() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppSize(MaxDownloadBytes + 1).Build(),
            "app.size");

    [Fact]
    public void RejectsAppArchiveShaNotHex() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppArchiveSha256(new string('g', 64)).Build(),
            "app.archiveSha256");

    [Fact]
    public void RejectsAppArchiveShaWrongLength() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppArchiveSha256(new string('a', 63)).Build(),
            "app.archiveSha256");

    [Fact]
    public void RejectsAppImageIdWithoutPrefix() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppImageId(TestReleases.Hex('b')).Build(),
            "app.imageId");

    [Fact]
    public void RejectsAppLoadedTagMismatch() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppLoadedTag("sub2api-report-app:9.9.9").Build(),
            "app.loadedTag");

    [Fact]
    public void RejectsAppArchiveUrlOutsideFixedRepo() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithAppArchiveUrl("https://evil.example/app.tar.gz").Build(),
            "app.archiveUrl");

    [Fact]
    public void RejectsAppArchiveUrlWithWrongTag() =>
        AssertRejected(
            new ReleaseManifestBuilder()
                .WithAppArchiveUrl(
                    "https://github.com/lohaaa/sub2api-report/releases/download/v9.9.9/sub2api-report-app-v1.2.0-linux-amd64.tar.gz")
                .Build(),
            "app.archiveUrl");

    [Fact]
    public void RejectsUpdaterSelfUpdateSupported() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithSelfUpdateSupported(true).Build(),
            "updater.selfUpdateSupported");

    [Fact]
    public void RejectsUpdaterLoadedTagMismatch() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithUpdaterLoadedTag("sub2api-report-updater:9.9.9").Build(),
            "updater.loadedTag");

    [Fact]
    public void RejectsUpdaterImageIdInvalid() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithUpdaterImageId("sha256:short").Build(),
            "updater.imageId");

    [Fact]
    public void RejectsUpdaterArchiveUrlOutsideFixedRepo() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithUpdaterArchiveUrl("https://evil.example/updater.tar.gz").Build(),
            "updater.archiveUrl");

    [Fact]
    public void RejectsUpdaterSizeOverLimit() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithUpdaterSize(MaxDownloadBytes + 1).Build(),
            "updater.size");

    [Fact]
    public void RejectsInvalidTargetMigration() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithTargetMigration("latest").Build(),
            "database.targetMigration");

    [Fact]
    public void RejectsReleaseNotesPageUrlMismatch() =>
        AssertRejected(
            new ReleaseManifestBuilder()
                .WithReleaseNotesPageUrl("https://github.com/evil/sub2api-report/releases/tag/v1.2.0")
                .Build(),
            "releaseNotes.pageUrl");

    [Fact]
    public void RejectsReleaseNotesAssetUrlOutsideFixedRepo() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithReleaseNotesAssetUrl("https://evil.example/notes.md").Build(),
            "releaseNotes.assetUrl");

    [Fact]
    public void RejectsReleaseNotesShaInvalid() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithReleaseNotesSha256("nothex").Build(),
            "releaseNotes.sha256");

    [Fact]
    public void RejectsReleaseNotesSizeOverLimit() =>
        AssertRejected(
            new ReleaseManifestBuilder().WithReleaseNotesSize(MaxMetadataBytes + 1).Build(),
            "releaseNotes.size");

    [Fact]
    public void RejectsUnknownJsonField()
    {
        var manifest = new ReleaseManifestBuilder().Build();
        var json = JsonNode.Parse(JsonSerializer.Serialize(manifest))!;
        json["sneakyField"] = 1;
        var tampered = json.ToJsonString();

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ReleaseManifest>(tampered));
    }

    private static void AssertRejected(
        ReleaseManifest manifest,
        string expectedFragment,
        bool expectEmpty = false)
    {
        var errors = ReleaseManifestValidator.Validate(manifest, MaxDownloadBytes, MaxMetadataBytes, Now);
        if (expectEmpty)
        {
            Assert.Empty(errors);
            return;
        }

        Assert.Contains(errors, error => error.Contains(expectedFragment, StringComparison.Ordinal));
    }
}
