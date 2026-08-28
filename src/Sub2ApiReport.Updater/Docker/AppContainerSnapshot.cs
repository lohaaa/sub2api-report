using System.Text.Json.Serialization;

namespace Sub2ApiReport.Updater.Docker;

/// <summary>App 容器端口绑定快照。</summary>
public sealed record AppPortBinding(string ContainerPort, string? HostIp, string? HostPort);

/// <summary>App 容器挂载点快照（named volume 或 bind）。</summary>
public sealed record AppMount(string Type, string? Name, string? Source, string Target, bool ReadOnly);

/// <summary>App 容器网络配置快照。</summary>
public sealed record AppNetworkAttachment(string Network, IReadOnlyList<string> Aliases);

/// <summary>App 容器重启策略快照。</summary>
public sealed record AppRestartPolicy(string? Name, long MaximumRetryCount);

/// <summary>App 容器资源限制快照（deployment contract v1 关注的子集）。</summary>
public sealed record AppResourceLimits(
    long CpuShares,
    long NanoCpus,
    long Memory,
    long CpuPeriod,
    long CpuQuota,
    long MemoryReservation,
    long MemorySwap,
    long? PidsLimit);

/// <summary>App 容器健康检查快照。</summary>
public sealed record AppHealthcheck(
    IReadOnlyList<string> Test,
    TimeSpan Interval,
    TimeSpan Timeout,
    long StartPeriod,
    long Retries);

/// <summary>
/// 当前 App 容器配置快照：替换与回滚时只保留显式当前部署契约
/// （env、named 数据挂载、网络、端口绑定、restart/security/resource/health 配置）。
/// 可 JSON 序列化并持久化到操作状态，用于 Updater 重启后的回滚重建。
/// </summary>
public sealed record AppContainerSnapshot(
    string ContainerId,
    string ContainerName,
    string ImageId,
    string? CurrentImageTag,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> Env,
    string? User,
    string? WorkingDir,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Cmd,
    string? StopSignal,
    TimeSpan? StopTimeout,
    IReadOnlyList<AppPortBinding> PortBindings,
    IReadOnlyDictionary<string, bool> ExposedPorts,
    IReadOnlyList<AppMount> Mounts,
    IReadOnlyList<string> Binds,
    string? NetworkMode,
    IReadOnlyList<AppNetworkAttachment> Networks,
    AppRestartPolicy? RestartPolicy,
    IReadOnlyList<string> SecurityOptions,
    bool Privileged,
    bool ReadonlyRootfs,
    AppResourceLimits Resources,
    AppHealthcheck? Healthcheck,
    IReadOnlyList<string> ExtraHosts,
    IReadOnlyDictionary<string, string> LogConfig,
    IReadOnlyDictionary<string, string> Tmpfs,
    IReadOnlyDictionary<string, string> Sysctls);
