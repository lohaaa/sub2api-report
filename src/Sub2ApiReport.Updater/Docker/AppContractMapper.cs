using Docker.DotNet.Models;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Updater.Docker;

/// <summary>
/// Docker API 模型与 App 容器契约快照之间的纯映射/校验逻辑，无副作用，便于测试注入。
/// 只保留显式当前部署契约（deployment contract v1）需要的配置，禁止把任意宿主机配置透传。
/// </summary>
public static class AppContractMapper
{
    public static AppContainerSnapshot MapSnapshot(ContainerInspectResponse inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        var config = inspect.Config ?? throw InvalidContract("容器缺少 Config。");
        var hostConfig = inspect.HostConfig ?? throw InvalidContract("容器缺少 HostConfig。");
        var name = NormalizeContainerName(inspect.Name);
        var currentTag = config.Image is { Length: > 0 } createdImage
            && !createdImage.StartsWith("sha256:", StringComparison.Ordinal)
                ? createdImage
                : null;

        var networks = (inspect.NetworkSettings?.Networks ?? new Dictionary<string, EndpointSettings>())
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .Select(kvp => new AppNetworkAttachment(
                kvp.Key,
                (kvp.Value?.Aliases ?? []).ToList()))
            .ToList();

        var mounts = (inspect.Mounts ?? [])
            .Select(mount => new AppMount(
                mount.Type ?? string.Empty,
                mount.Name,
                mount.Source,
                mount.Destination ?? string.Empty,
                !mount.RW))
            .Where(mount => !string.IsNullOrWhiteSpace(mount.Target))
            .ToList();

        return new AppContainerSnapshot(
            inspect.ID,
            name,
            NormalizeImageId(inspect.Image),
            currentTag,
            CopyDictionary(config.Labels),
            (config.Env ?? []).ToList(),
            config.User,
            config.WorkingDir,
            (config.Entrypoint ?? []).ToList(),
            (config.Cmd ?? []).ToList(),
            config.StopSignal,
            config.StopTimeout,
            MapPortBindings(hostConfig.PortBindings),
            CopyDictionary(config.ExposedPorts?.ToDictionary(kvp => kvp.Key, _ => true)),
            mounts,
            (hostConfig.Binds ?? []).ToList(),
            hostConfig.NetworkMode,
            networks,
            hostConfig.RestartPolicy?.Name is null
                ? null
                : new AppRestartPolicy(
                    hostConfig.RestartPolicy.Name.ToString(),
                    hostConfig.RestartPolicy.MaximumRetryCount),
            (hostConfig.SecurityOpt ?? []).ToList(),
            hostConfig.Privileged,
            hostConfig.ReadonlyRootfs,
            MapResources(hostConfig),
            MapHealthcheck(config.Healthcheck),
            (hostConfig.ExtraHosts ?? []).ToList(),
            hostConfig.LogConfig is null
                ? new Dictionary<string, string>()
                : CopyDictionary(hostConfig.LogConfig.Config),
            CopyDictionary(hostConfig.Tmpfs),
            CopyDictionary(hostConfig.Sysctls));
    }

    public static CreateContainerParameters ToCreateParameters(
        AppContainerSnapshot snapshot,
        string imageId,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var labels = snapshot.Labels.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        labels[UpdateContractConstants.UpgradeOperationLabelKey] = operationId;

        var environment = snapshot.Env
            .Where(value => !value.StartsWith(
                $"{UpdateContractConstants.MaintenanceOperationEnvironmentKey}=",
                StringComparison.Ordinal))
            .ToList();
        environment.Add($"{UpdateContractConstants.MaintenanceOperationEnvironmentKey}={operationId}");

        var hostConfig = new HostConfig
        {
            Binds = [.. snapshot.Binds],
            NetworkMode = snapshot.NetworkMode,
            PortBindings = snapshot.PortBindings.ToDictionary(
                binding => binding.ContainerPort,
                binding => (IList<PortBinding>)
                [
                    new PortBinding { HostIP = binding.HostIp, HostPort = binding.HostPort },
                ]),
            RestartPolicy = snapshot.RestartPolicy is null
                ? null
                : new RestartPolicy
                {
                    Name = Enum.TryParse<RestartPolicyKind>(
                        snapshot.RestartPolicy.Name,
                        ignoreCase: true,
                        out var kind)
                        ? kind
                        : RestartPolicyKind.No,
                    MaximumRetryCount = snapshot.RestartPolicy.MaximumRetryCount,
                },
            SecurityOpt = [.. snapshot.SecurityOptions],
            Privileged = snapshot.Privileged,
            ReadonlyRootfs = snapshot.ReadonlyRootfs,
            ExtraHosts = [.. snapshot.ExtraHosts],
            Tmpfs = new Dictionary<string, string>(snapshot.Tmpfs),
            Sysctls = new Dictionary<string, string>(snapshot.Sysctls),
            Mounts = BuildReplayedMounts(snapshot),
        };
        ApplyResources(hostConfig, snapshot.Resources);

        if (snapshot.LogConfig.Count > 0)
        {
            hostConfig.LogConfig = new LogConfig { Config = new Dictionary<string, string>(snapshot.LogConfig) };
        }

        return new CreateContainerParameters
        {
            Name = snapshot.ContainerName,
            Image = imageId,
            Labels = labels,
            Env = environment,
            User = snapshot.User,
            WorkingDir = snapshot.WorkingDir,
            Entrypoint = snapshot.Entrypoint.Count > 0 ? [.. snapshot.Entrypoint] : null,
            Cmd = snapshot.Cmd.Count > 0 ? [.. snapshot.Cmd] : null,
            StopSignal = snapshot.StopSignal,
            StopTimeout = snapshot.StopTimeout,
            ExposedPorts = snapshot.ExposedPorts.ToDictionary(kvp => kvp.Key, _ => new EmptyStruct()),
            Healthcheck = snapshot.Healthcheck is null
                ? null
                : new HealthConfig
                {
                    Test = [.. snapshot.Healthcheck.Test],
                    Interval = snapshot.Healthcheck.Interval,
                    Timeout = snapshot.Healthcheck.Timeout,
                    StartPeriod = snapshot.Healthcheck.StartPeriod,
                    Retries = snapshot.Healthcheck.Retries,
                },
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = snapshot.Networks.ToDictionary(
                    network => network.Network,
                    network => new EndpointSettings { Aliases = [.. network.Aliases] }),
            },
            HostConfig = hostConfig,
        };
    }

    /// <summary>
    /// 校验当前 App 容器是否符合 deployment contract v1。任何不满足项都会阻止安装。
    /// </summary>
    public static IReadOnlyList<string> ValidateContract(AppContainerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(snapshot.ContainerId) || string.IsNullOrWhiteSpace(snapshot.ContainerName))
        {
            errors.Add("App 容器缺少 ID 或名称。");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ImageId))
        {
            errors.Add("App 容器缺少镜像 ID。");
        }

        if (!snapshot.Labels.TryGetValue(UpdateContractConstants.AppRoleLabelKey, out var role)
            || !string.Equals(role, UpdateContractConstants.AppRoleLabelValue, StringComparison.Ordinal))
        {
            errors.Add("App 容器角色标签无效。");
        }

        if (!snapshot.Labels.TryGetValue(UpdateContractConstants.InstanceLabelKey, out var instanceId)
            || string.IsNullOrWhiteSpace(instanceId))
        {
            errors.Add("App 容器实例标签无效。");
        }

        if (!snapshot.Labels.TryGetValue(UpdateContractConstants.ContractLabelKey, out var contract)
            || !string.Equals(
                contract,
                UpdateContractConstants.DeploymentContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            errors.Add("App 容器部署契约标签无效。");
        }

        if (snapshot.Mounts.All(mount => mount.Target != UpdateContractConstants.AppDataMountTarget))
        {
            errors.Add("App 容器缺少数据卷挂载点 " + UpdateContractConstants.AppDataMountTarget + "。");
        }

        if (snapshot.Networks.Count == 0)
        {
            errors.Add("App 容器未连接任何 Compose 网络。");
        }

        if (snapshot.ExposedPorts.All(port => !port.Key.StartsWith(
                UpdateContractConstants.AppInternalPort + "/",
                StringComparison.Ordinal)))
        {
            errors.Add("App 容器未暴露内部端口 " + UpdateContractConstants.AppInternalPort + "。");
        }

        if (snapshot.Healthcheck is null)
        {
            errors.Add("App 容器缺少健康检查配置。");
        }

        if (snapshot.RestartPolicy is null || IsNoRestartPolicy(snapshot.RestartPolicy.Name))
        {
            errors.Add("App 容器缺少重启策略。");
        }

        return errors;
    }

    private static bool IsNoRestartPolicy(string? name) =>
        string.IsNullOrEmpty(name)
        || name.Equals("Undefined", StringComparison.Ordinal)
        || name.Equals("No", StringComparison.OrdinalIgnoreCase);

    private static AppResourceLimits MapResources(HostConfig resources) => new(
        resources.CPUShares,
        resources.NanoCPUs,
        resources.Memory,
        resources.CPUPeriod,
        resources.CPUQuota,
        resources.MemoryReservation,
        resources.MemorySwap,
        resources.PidsLimit);

    private static AppHealthcheck? MapHealthcheck(HealthConfig? healthcheck) =>
        healthcheck is null
            ? null
            : new AppHealthcheck(
                [.. healthcheck.Test],
                healthcheck.Interval,
                healthcheck.Timeout,
                healthcheck.StartPeriod,
                healthcheck.Retries);

    private static List<AppPortBinding> MapPortBindings(
        IDictionary<string, IList<PortBinding>>? portBindings)
    {
        if (portBindings is null)
        {
            return [];
        }

        return portBindings
            .SelectMany(kvp => (kvp.Value ?? []).Select(binding => new AppPortBinding(
                kvp.Key,
                binding?.HostIP,
                binding?.HostPort)))
            .ToList();
    }

    /// <summary>
    /// 生成候选/回滚容器创建用的 HostConfig.Mounts：Binds 中已覆盖的挂载目标不再通过
    /// Mounts 重复下发（Docker Engine 会因 duplicate mount point 拒绝创建，官方 Compose
    /// 的 named volume + 只读 token bind 即命中该冲突）；named volume 和只通过 --mount
    /// 表达的 bind/tmpfs 保持原契约重放。快照无法安全解析或最终目标重复时，抛出脱敏的
    /// 安全错误，不发送创建请求。
    /// </summary>
    private static List<Mount> BuildReplayedMounts(AppContainerSnapshot snapshot)
    {
        var bindTargets = ParseBindTargets(snapshot.Binds);
        var replayedMounts = new List<Mount>();
        foreach (var mount in snapshot.Mounts)
        {
            if (string.IsNullOrWhiteSpace(mount.Target)
                || !mount.Target.StartsWith('/'))
            {
                throw new UpdateOperationException(
                    StatusCodes.Status409Conflict,
                    "App 容器快照包含无效挂载点，拒绝生成容器创建请求。");
            }

            if (bindTargets.Contains(NormalizeMountTarget(mount.Target)))
            {
                // 该目标已由 Binds 原样保留（含读写标志与 propagation 选项），不再重复下发。
                continue;
            }

            replayedMounts.Add(ToDockerMount(mount));
        }

        // 防御性校验：最终发送的 Binds + Mounts 挂载目标必须唯一，冲突时拒绝发请求。
        var usedTargets = new HashSet<string>(bindTargets, StringComparer.Ordinal);
        foreach (var mount in replayedMounts)
        {
            if (!usedTargets.Add(NormalizeMountTarget(mount.Target)))
            {
                throw new UpdateOperationException(
                    StatusCodes.Status409Conflict,
                    "App 容器快照中挂载点 " + mount.Target + " 重复声明，拒绝生成容器创建请求。");
            }
        }

        return replayedMounts;
    }

    private static HashSet<string> ParseBindTargets(IReadOnlyList<string> binds)
    {
        var bindTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bind in binds)
        {
            if (string.IsNullOrWhiteSpace(bind))
            {
                continue;
            }

            if (!TryParseBindTarget(bind, out var target)
                || !bindTargets.Add(NormalizeMountTarget(target)))
            {
                throw new UpdateOperationException(
                    StatusCodes.Status409Conflict,
                    "App 容器快照中的 Binds 声明无法安全解析或挂载点目标重复，拒绝生成容器创建请求。");
            }
        }

        return bindTargets;
    }

    /// <summary>
    /// 解析 Linux 短语法 <c>[SOURCE:]TARGET[:MODE1[:MODE2...]]</c>（bind 与 named volume）
    /// 的挂载目标。目标必须是绝对路径；仅从末尾剥离只含选项字符的冒号分组，不把含非法
    /// 选项字符的冒号路径误判为选项。返回 target 未归一化，比对前需经 NormalizeMountTarget。
    /// </summary>
    internal static bool TryParseBindTarget(string? bind, out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(bind))
        {
            return false;
        }

        var parts = bind.Split(':');
        var end = parts.Length;
        while (end > 1 && IsBindModeComponent(parts[end - 1]))
        {
            end--;
        }

        if (end > 2)
        {
            // 既不是 [SOURCE:]TARGET 也不是 TARGET，无法安全定位目标。
            return false;
        }

        var candidate = end == 2 ? parts[1] : parts[0];
        if (!candidate.StartsWith('/'))
        {
            return false;
        }

        target = candidate;
        return true;
    }

    private static bool IsBindModeComponent(string component) =>
        component.Length > 0
        && component[0] != '/'
        && component.All(IsBindModeCharacter);

    private static bool IsBindModeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is ',' or '=' or '.' or '_' or '-' or '+';

    /// <summary>归一化挂载点目标用于比对：去除首尾空白和末尾斜杠，根路径保持 "/"。</summary>
    internal static string NormalizeMountTarget(string target)
    {
        var normalized = target.Trim().TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static Mount ToDockerMount(AppMount mount) => new()
    {
        Type = mount.Type,
        // named volume 的创建契约要求 volume 名称；inspect 顶层 Mounts 的 Source 是宿主机数据路径。
        Source = IsVolumeMount(mount) ? mount.Name ?? mount.Source : mount.Source,
        Target = mount.Target,
        ReadOnly = mount.ReadOnly,
    };

    private static bool IsVolumeMount(AppMount mount) =>
        string.Equals(mount.Type, "volume", StringComparison.OrdinalIgnoreCase);

    private static void ApplyResources(HostConfig hostConfig, AppResourceLimits limits)
    {
        hostConfig.CPUShares = limits.CpuShares;
        hostConfig.NanoCPUs = limits.NanoCpus;
        hostConfig.Memory = limits.Memory;
        hostConfig.CPUPeriod = limits.CpuPeriod;
        hostConfig.CPUQuota = limits.CpuQuota;
        hostConfig.MemoryReservation = limits.MemoryReservation;
        hostConfig.MemorySwap = limits.MemorySwap;
        hostConfig.PidsLimit = limits.PidsLimit;
    }

    private static string NormalizeContainerName(string? name) =>
        (name ?? string.Empty).TrimStart('/');

    private static string NormalizeImageId(string? image) => image ?? string.Empty;

    private static Dictionary<string, TValue> CopyDictionary<TValue>(
        IDictionary<string, TValue>? source) =>
        source is null
            ? new Dictionary<string, TValue>()
            : new Dictionary<string, TValue>(source);

    private static UpdateOperationException InvalidContract(string message) =>
        new(StatusCodes.Status409Conflict, message);
}
