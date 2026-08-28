using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests;

/// <summary>
/// Docker App 管理替身：记录调用、可注入各阶段失败。不依赖真实 Docker。
/// </summary>
internal sealed class FakeDockerAppManager : IDockerAppManager
{
    public FakeDockerAppManager()
    {
        KnownContainerIds.Add("app-container-1");
    }

    /// <summary>容器存在的 ID 集合（FindContainerByIdAsync 据此返回 null 或快照）。</summary>
    public HashSet<string> KnownContainerIds { get; } = [];
    public int CallCount;
    public Func<CancellationToken, Task<AppContainerSnapshot?>>? OnFindAppContainer { get; set; }
    public Func<Stream, string, Task>? OnLoadImage { get; set; }
    public Exception? OnCreateContainer { get; set; }

    /// <summary>前 N 次 CreateAppContainerAsync 抛出冲突异常，之后成功。</summary>
    public int CreateContainerFailTimes { get; set; }

    private int _createCalls;
    public Exception? OnStartContainer { get; set; }
    public Exception? OnTagImage { get; set; }
    public Exception? OnStopContainer { get; set; }
    public Exception? OnRemoveContainer { get; set; }
    public Exception? OnRenameContainer { get; set; }

    public List<string> StartedContainers { get; } = [];
    public List<string> RemovedContainers { get; } = [];
    public List<string> StoppedContainers { get; } = [];
    public List<(string ImageId, string Repository, string Tag)> TaggedImages { get; } = [];
    public List<(AppContainerSnapshot Contract, string ImageId, string OperationId)> CreatedContainers { get; } = [];
    public List<string> RenamedContainers { get; } = [];
    public bool LoadedArchive;

    public Task<AppContainerSnapshot?> FindAppContainerAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        return OnFindAppContainer is not null
            ? OnFindAppContainer(cancellationToken)
            : Task.FromResult<AppContainerSnapshot?>(TestSnapshots.CreateValidSnapshot());
    }

    public Task<AppContainerSnapshot?> FindContainerByIdAsync(string containerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<AppContainerSnapshot?>(
            !RemovedContainers.Contains(containerId) && KnownContainerIds.Contains(containerId)
                ? TestSnapshots.CreateValidSnapshot(containerId)
                : null);
    }

    public Task<AppContainerSnapshot?> FindContainerByOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        AppContainerSnapshot? snapshot = CreatedContainers.Count > 0
            ? TestSnapshots.CreateValidSnapshot(CreatedContainers[^1].Contract.ContainerId)
            : null;
        return Task.FromResult(snapshot);
    }

    public async Task<string> LoadImageArchiveAsync(
        Stream archiveStream,
        string expectedImageId,
        string expectedLoadedTag,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        if (OnLoadImage is not null)
        {
            await OnLoadImage(archiveStream, expectedImageId);
        }
        else
        {
            await archiveStream.CopyToAsync(Stream.Null, cancellationToken);
        }

        LoadedArchive = true;
        return expectedImageId;
    }

    public Task TagImageAsync(string imageId, string repository, string tag, CancellationToken cancellationToken)
    {
        if (OnTagImage is not null)
        {
            throw OnTagImage;
        }

        TaggedImages.Add((imageId, repository, tag));
        return Task.CompletedTask;
    }

    public Task<string> CreateAppContainerAsync(
        AppContainerSnapshot contract,
        string imageId,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (OnCreateContainer is not null)
        {
            throw OnCreateContainer;
        }

        if (CreateContainerFailTimes > 0 && Interlocked.Increment(ref _createCalls) <= CreateContainerFailTimes)
        {
            throw new UpdateOperationException(
                StatusCodes.Status409Conflict,
                "候选容器创建失败（模拟一次性失败）。");
        }

        CreatedContainers.Add((contract, imageId, operationId));
        return Task.FromResult($"candidate-{CreatedContainers.Count}");
    }

    public Task StartContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        if (OnStartContainer is not null)
        {
            throw OnStartContainer;
        }

        StartedContainers.Add(containerId);
        return Task.CompletedTask;
    }

    public Task StopContainerAsync(string containerId, int waitSeconds, CancellationToken cancellationToken)
    {
        if (OnStopContainer is not null)
        {
            throw OnStopContainer;
        }

        StoppedContainers.Add(containerId);
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        if (OnRemoveContainer is not null)
        {
            throw OnRemoveContainer;
        }

        RemovedContainers.Add(containerId);
        return Task.CompletedTask;
    }

    public Task RenameContainerAsync(string containerId, string newName, CancellationToken cancellationToken)
    {
        if (OnRenameContainer is not null)
        {
            throw OnRenameContainer;
        }

        RenamedContainers.Add(newName);
        return Task.CompletedTask;
    }
}

/// <summary>App 维护握手替身：可配置握手响应与阶段失败。</summary>
internal sealed class FakeMaintenanceClient : IAppMaintenanceClient
{
    public string Version { get; set; } = TestReleases.CurrentAppVersion;

    /// <summary>解除维护模式后的握手版本（模拟候选 App 已替换成功）。</summary>
    public string? VersionAfterComplete { get; set; }

    public bool MaintenanceMode { get; set; }

    public string? OperationId { get; set; }

    public string MaintenanceState => MaintenanceMode ? "maintenance" : "normal";
    public int DeploymentContractVersion { get; set; } = UpdateContractConstants.DeploymentContractVersion;

    public Exception? OnEnterMaintenance { get; set; }

    public Exception? OnCompleteMaintenance { get; set; }

    public int EnterCount;

    public int CompleteCount;

    public Task<AppUpdateHandshakeResponse> GetHandshakeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AppUpdateHandshakeResponse(
            CompleteCount > 0 && VersionAfterComplete is not null ? VersionAfterComplete : Version,
            DeploymentContractVersion,
            MaintenanceMode,
            MaintenanceState,
            OperationId,
            "test-migration"));

    public Task EnterMaintenanceAsync(string operationId, CancellationToken cancellationToken)
    {
        if (OnEnterMaintenance is not null)
        {
            throw OnEnterMaintenance;
        }

        Interlocked.Increment(ref EnterCount);
        MaintenanceMode = true;
        OperationId = operationId;
        return Task.CompletedTask;
    }

    public Task CompleteMaintenanceAsync(string operationId, CancellationToken cancellationToken)
    {
        if (OnCompleteMaintenance is not null)
        {
            throw OnCompleteMaintenance;
        }

        Interlocked.Increment(ref CompleteCount);
        MaintenanceMode = false;
        OperationId = null;
        return Task.CompletedTask;
    }
}

/// <summary>健康验证替身：可配置结果与验证期间抛出的异常。</summary>
internal sealed class FakeHealthVerifier : IHealthVerifier
{
    /// <summary>前 N 次验证失败，之后成功；默认总是成功。</summary>
    public int FailTimes;

    public bool AlwaysFail { get; set; }

    public string? FailureReason { get; set; }

    public Exception? OnVerify { get; set; }

    private int _remainingCalls;

    public List<(string Version, bool MaintenanceMode)> Calls { get; } = [];

    public Task<HealthVerificationResult> VerifyAsync(
        string expectedVersion,
        bool expectedMaintenanceMode,
        string? expectedOperationId,
        int requiredConsecutiveSuccesses,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (OnVerify is not null)
        {
            throw OnVerify;
        }

        Calls.Add((expectedVersion, expectedMaintenanceMode));
        var shouldFail = AlwaysFail
            || (FailTimes > 0 && Interlocked.Increment(ref _remainingCalls) <= FailTimes);
        return Task.FromResult(shouldFail
            ? new HealthVerificationResult(false, 0, FailureReason ?? "验证失败。")
            : new HealthVerificationResult(true, requiredConsecutiveSuccesses, null));
    }
}

/// <summary>SQLite 备份替身：可注入失败，可记录恢复调用。</summary>
internal sealed class FakeBackupService : ISqliteBackupService
{
    public Exception? OnCreateBackup { get; set; }
    public Exception? OnRestoreBackup { get; set; }
    public int CreateCount;
    public int RestoreCount;
    public List<(string OperationId, string BackupFilePath, string ExpectedSha256)> Restores { get; } = [];
    public Func<string, SqliteBackupResult>? OnCreateBackupResult { get; set; }

    public Task<SqliteBackupResult> CreateBackupAsync(string operationId, CancellationToken cancellationToken)
    {
        if (OnCreateBackup is not null)
        {
            throw OnCreateBackup;
        }

        Interlocked.Increment(ref CreateCount);
        return Task.FromResult(OnCreateBackupResult is not null
            ? OnCreateBackupResult(operationId)
            : new SqliteBackupResult($"/tmp/{operationId}.db", 1024, TestReleases.Hex('f')));
    }

    public Task RestoreBackupAsync(
        string operationId,
        string backupFilePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (OnRestoreBackup is not null)
        {
            throw OnRestoreBackup;
        }

        Interlocked.Increment(ref RestoreCount);
        Restores.Add((operationId, backupFilePath, expectedSha256));
        return Task.CompletedTask;
    }
}

/// <summary>启动恢复替身（默认注册到 WebApplicationFactory，避免测试触碰真实 Docker）。</summary>
internal sealed class FakeInstallRecovery : IInstallRecovery
{
    public Task RecoverAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>不执行任何操作的事务替身（用于门禁/队列测试）。</summary>
internal sealed class FakeInstallTransaction : IInstallTransaction
{
    public Func<InstallOperationRecord, InstallOperationRecord>? OnExecute { get; set; }
    public List<InstallOperationRecord> Executed { get; } = [];

    public Task<InstallOperationRecord> ExecuteAsync(
        InstallOperationRecord operation,
        CancellationToken cancellationToken)
    {
        Executed.Add(operation);
        var result = OnExecute?.Invoke(operation)
            ?? operation with
            {
                State = InstallOperationStates.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        return Task.FromResult(result);
    }
}

/// <summary>构造满足 deployment contract v1 的 App 容器快照。</summary>
internal static class TestSnapshots
{
    public static AppContainerSnapshot CreateValidSnapshot(
        string containerId = "app-container-1",
        string containerName = "sub2api-report-app-1",
        string? imageId = null,
        string? imageTag = "sub2api-report-app:0.7.0") => new(
        ContainerId: containerId,
        ContainerName: containerName,
        ImageId: imageId ?? "sha256:" + TestReleases.Hex('0'),
        CurrentImageTag: imageTag,
        Labels: new Dictionary<string, string>
        {
            [UpdateContractConstants.AppRoleLabelKey] = UpdateContractConstants.AppRoleLabelValue,
            [UpdateContractConstants.InstanceLabelKey] = "test-instance",
            [UpdateContractConstants.ContractLabelKey] =
                UpdateContractConstants.DeploymentContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        Env: ["ASPNETCORE_URLS=http://+:8080"],
        User: null,
        WorkingDir: "/app",
        Entrypoint: ["/app/entrypoint.sh"],
        Cmd: [],
        StopSignal: "SIGTERM",
        StopTimeout: TimeSpan.FromSeconds(10),
        PortBindings: [new AppPortBinding("8080/tcp", null, null)],
        ExposedPorts: new Dictionary<string, bool> { ["8080/tcp"] = true },
        Mounts:
        [
            new AppMount("volume", "sub2api-report-data", null, UpdateContractConstants.AppDataMountTarget, false),
        ],
        Binds: [],
        NetworkMode: null,
        Networks:
        [
            new AppNetworkAttachment("sub2api-report_default", ["app"]),
        ],
        RestartPolicy: new AppRestartPolicy("UnlessStopped", 0),
        SecurityOptions: ["no-new-privileges"],
        Privileged: false,
        ReadonlyRootfs: false,
        Resources: new AppResourceLimits(0, 0, 0, 0, 0, 0, 0, null),
        Healthcheck: new AppHealthcheck(
            ["CMD", "wget", "-q", "--spider", "http://localhost:8080/health/live"],
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            30,
            3),
        ExtraHosts: [],
        LogConfig: new Dictionary<string, string>(),
        Tmpfs: new Dictionary<string, string>(),
        Sysctls: new Dictionary<string, string>());

    public static InstallOperationRecord CreateOperation(
        string currentVersion = TestReleases.CurrentAppVersion,
        string targetVersion = TestReleases.DefaultVersion,
        string state = InstallOperationStates.Queued,
        bool maintenanceEntered = false,
        bool backupCompleted = false,
        string? backupFilePath = null,
        string? backupSha256 = null,
        AppContainerSnapshot? oldSnapshot = null,
        string? oldImageId = null,
        string? candidateContainerId = null) => new(
        OperationId: Guid.NewGuid().ToString("N"),
        State: state,
        CurrentVersion: currentVersion,
        TargetVersion: targetVersion,
        CreatedAt: DateTimeOffset.UtcNow.AddSeconds(-30),
        UpdatedAt: DateTimeOffset.UtcNow,
        CompletedAt: null,
        LastError: null,
        MaintenanceEntered: maintenanceEntered,
        BackupCompleted: backupCompleted,
        PreflightReport: InstallPreflightReport.Empty(),
        ArchiveFilePath: null,
        ArchiveSha256: null,
        LoadedImageId: "sha256:" + TestReleases.Hex('b'),
        BackupFilePath: backupFilePath,
        BackupSha256: backupSha256,
        OldContainerId: oldSnapshot?.ContainerId,
        OldContainerName: oldSnapshot?.ContainerName,
        OldImageId: oldImageId ?? oldSnapshot?.ImageId,
        CandidateContainerId: candidateContainerId,
        OldContainerSnapshot: oldSnapshot,
        Stages: []);

    public static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
