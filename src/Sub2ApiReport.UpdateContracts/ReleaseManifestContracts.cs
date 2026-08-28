using System.Text.Json.Serialization;

namespace Sub2ApiReport.UpdateContracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseManifest(
    int SchemaVersion,
    string Version,
    string Channel,
    DateTimeOffset PublishedAt,
    string Architecture,
    int DeploymentContractVersion,
    string MinimumUpdaterVersion,
    bool ManualUpgradeRequired,
    bool OnlineInstallSupported,
    string SignatureAlgorithm,
    ReleaseAppArtifact App,
    ReleaseUpdaterArtifact Updater,
    ReleaseDatabaseSection Database,
    ReleaseNotesSection ReleaseNotes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseAppArtifact(
    string ArchiveUrl,
    string ArchiveSha256,
    string ImageId,
    string LoadedTag,
    long Size);

/// <summary>
/// Updater 工件元数据，仅用于手工完整 bundle 的说明；在线路径不下载该工件，也不实现在线自更新。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseUpdaterArtifact(
    string ArchiveUrl,
    string ArchiveSha256,
    string ImageId,
    string LoadedTag,
    long Size,
    bool SelfUpdateSupported);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseDatabaseSection(
    string TargetMigration,
    bool RequiresBackupRestoreForRollback);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseNotesSection(
    string PageUrl,
    string AssetUrl,
    string Sha256,
    long Size);
