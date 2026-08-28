using System.Text.Json.Serialization;

namespace Sub2ApiReport.UpdateContracts;

/// <summary>
/// 安装请求：由调用方（App）提交。TargetVersion 必须与最近一次检查缓存的目标版本一致；
/// CurrentVersion 必须是有效 SemVer 且严格低于目标版本。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InstallUpdateRequest(string CurrentVersion, string? TargetVersion = null);

/// <summary>安装请求受理响应：操作进入后台队列，浏览器通过 App 轮询结果。</summary>
public sealed record InstallAcceptedResponse(string OperationId, string State);

/// <summary>安装操作的持久化状态（终态与非终态）。非终态在 Updater 重启后由启动恢复处理。</summary>
public static class InstallOperationStates
{
    public const string Queued = "queued";
    public const string Preflight = "preflight";
    public const string DownloadingArchive = "downloading_archive";
    public const string VerifyingArchive = "verifying_archive";
    public const string LoadingImage = "loading_image";
    public const string RequestingMaintenance = "requesting_maintenance";
    public const string BackingUp = "backing_up";
    public const string ReplacingApp = "replacing_app";
    public const string Verifying = "verifying";
    public const string CompletingMaintenance = "completing_maintenance";
    public const string Succeeded = "succeeded";
    public const string RollingBack = "rolling_back";
    public const string RolledBack = "rolled_back";
    public const string Failed = "failed";
    public const string FailedNeedsOperator = "failed_needs_operator";

    public static bool IsTerminal(string state) => state is Succeeded or RolledBack or Failed or FailedNeedsOperator;
}

/// <summary>Updater 请求 App 进入或完成维护模式。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppMaintenanceRequest(string OperationId);

/// <summary>App 对 Updater 的维护握手响应契约。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppUpdateHandshakeResponse(
    string Version,
    int DeploymentContractVersion,
    bool MaintenanceMode,
    string MaintenanceState,
    string? MaintenanceOperationId,
    string? MigrationIdentity);

/// <summary>单个安装阶段的持久化历史记录。</summary>
public sealed record InstallStageRecord(
    string Stage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? Error = null);

/// <summary>安装操作状态响应（供 App 轮询展示）。</summary>
public sealed record InstallOperationResponse(
    string OperationId,
    string State,
    string? Stage,
    string CurrentVersion,
    string TargetVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? LastError,
    IReadOnlyList<InstallStageRecord> Stages);

/// <summary>安装前检查（preflight）结果，持久化在操作记录中，供页面展示。</summary>
public sealed record InstallPreflightReport(
    bool ReleaseCacheValid,
    bool DockerReachable,
    bool CurrentAppReady,
    bool DatabaseAccessible,
    bool StateDirectoryWritable,
    IReadOnlyList<string> Errors)
{
    public static InstallPreflightReport Empty() =>
        new(false, false, false, false, false, []);
}
