using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Docker;

namespace Sub2ApiReport.Updater.State;

/// <summary>
/// 安装操作的持久化快照。写入 /update-state/operations/&lt;operation-id&gt;.json（原子替换），
/// 不包含凭证、报告内容或 GitHub token；Updater 重启后由启动恢复逻辑读取。
/// </summary>
public sealed record InstallOperationRecord(
    string OperationId,
    string State,
    string CurrentVersion,
    string TargetVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? LastError,
    bool MaintenanceEntered,
    bool BackupCompleted,
    InstallPreflightReport? PreflightReport,
    string? ArchiveFilePath,
    string? ArchiveSha256,
    string? LoadedImageId,
    string? BackupFilePath,
    string? BackupSha256,
    string? OldContainerId,
    string? OldContainerName,
    string? OldImageId,
    string? CandidateContainerId,
    AppContainerSnapshot? OldContainerSnapshot,
    IReadOnlyList<InstallStageRecord> Stages);
