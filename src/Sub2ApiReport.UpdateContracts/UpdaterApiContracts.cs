using System.Text.Json.Serialization;

namespace Sub2ApiReport.UpdateContracts;

/// <summary>检查请求：由调用方（App）提供当前 App 版本，Updater 据此判断是否有可用更新。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCheckRequest(string CurrentVersion);

public sealed record UpdaterStatusResponse(
    string Version,
    bool InstallationEnabled,
    string State,
    DateTimeOffset? LastCheckedAt,
    string? AvailableVersion,
    string? LastOperationId = null,
    string? LastOperationState = null);

public sealed record UpdateCheckResponse(
    bool UpdateAvailable,
    string CurrentVersion,
    string? AvailableVersion,
    DateTimeOffset? PublishedAt,
    bool ManualUpgradeRequired,
    string UpgradeMessage);

public sealed record UpdatePlanStep(
    int Order,
    string Name,
    string Description);

public sealed record UpdatePlanResponse(
    string CurrentVersion,
    string? TargetVersion,
    bool InstallationEnabled,
    bool ManualUpgradeRequired,
    string UpgradeMessage,
    IReadOnlyList<UpdatePlanStep> Steps);
