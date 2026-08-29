using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.Install;

public sealed class InstallTransactionTests : IDisposable
{
    private static readonly byte[] ArchiveBytes = [0x1f, 0x8b, 0x08, 0x00, 0x01];

    private readonly TempDirectory _temp = new();
    private readonly UpdateStateStore _stateStore;
    private readonly UpdateOptions _options;
    private readonly ReleaseCacheService _releaseCache;
    private readonly RSA _signingKey;
    private readonly FakeDockerAppManager _docker = new();
    private readonly FakeMaintenanceClient _maintenance = new();
    private readonly FakeBackupService _backup = new();
    private readonly FakeHealthVerifier _health = new();

    public InstallTransactionTests()
    {
        _stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        _options = new UpdateOptions
        {
            InstallationEnabled = true,
            DatabasePath = Path.Combine(_temp.FullPath, "db", "sub2api-report.db"),
            StatePath = Path.Combine(_temp.FullPath, "state"),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath)!);
        File.WriteAllText(_options.DatabasePath, "placeholder");

        var (key, publicPem) = TestKeys.CreateSigningKey();
        _signingKey = key;
        var publicKeyPath = Path.Combine(_temp.FullPath, "public.pem");
        File.WriteAllText(publicKeyPath, publicPem);
        _releaseCache = new ReleaseCacheService(
            _stateStore,
            new ReleasePublicKeyProvider(publicKeyPath),
            _options,
            TimeProvider.System);
        // 候选 App 替换后握手返回目标版本。
        _maintenance.VersionAfterComplete = TestReleases.DefaultVersion;
    }

    [Fact]
    public async Task SuccessfulTransactionPersistsAllStagesAndReachesSucceeded()
    {
        _docker.LoadedImageIdOverride = "sha256:" + TestReleases.Hex('f');
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Succeeded, result.State);
        Assert.Null(result.LastError);
        Assert.True(_docker.LoadedArchive);
        Assert.Equal("sha256:" + TestReleases.Hex('f'), result.LoadedImageId);
        Assert.Equal(1, _maintenance.EnterCount);
        Assert.Equal(1, _maintenance.CompleteCount);
        Assert.Equal(1, _backup.CreateCount);
        Assert.Single(_health.Calls);
        Assert.Equal((operation.TargetVersion, true), _health.Calls[0]);
        Assert.Contains(
            (UpdateContractConstants.AppCurrentImageRepository, UpdateContractConstants.AppCurrentImageTagName),
            _docker.TaggedImages.Select(tag => (tag.Repository, tag.Tag)));
        Assert.Single(_docker.CreatedContainers);
        Assert.Equal(result.LoadedImageId, _docker.CreatedContainers[0].ImageId);
        Assert.Equal(result.OldContainerId, _docker.CreatedContainers[0].Contract.ContainerId);
        Assert.Single(_docker.RemovedContainers);
        Assert.Contains(_docker.RenamedContainers, name => name.Contains("-old-", StringComparison.Ordinal));
        Assert.Equal(
            [
                InstallOperationStates.Preflight,
                InstallOperationStates.DownloadingArchive,
                InstallOperationStates.VerifyingArchive,
                InstallOperationStates.LoadingImage,
                InstallOperationStates.RequestingMaintenance,
                InstallOperationStates.BackingUp,
                InstallOperationStates.ReplacingApp,
                InstallOperationStates.Verifying,
                InstallOperationStates.CompletingMaintenance,
            ],
            result.Stages.Select(stage => stage.Stage));
        Assert.All(result.Stages, stage => Assert.NotNull(stage.CompletedAt));

        var persisted = await _stateStore.LoadOperationAsync(result.OperationId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(InstallOperationStates.Succeeded, persisted!.State);
        Assert.True(persisted.BackupCompleted);
        Assert.True(persisted.MaintenanceEntered);
        Assert.NotNull(persisted.OldContainerSnapshot);
        Assert.NotNull(persisted.PreflightReport);
        Assert.True(persisted.PreflightReport!.DockerReachable);
        Assert.True(persisted.PreflightReport!.CurrentAppReady);
    }

    [Fact]
    public async Task DownloadFailureBeforeBackupMarksFailedWithoutMaintenance()
    {
        var (transaction, operation) = await CreateTransactionAndOperationAsync(
            new FixedArchiveDownloader(throwOnDownload: true));

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.False(result.MaintenanceEntered);
        Assert.Equal(0, _maintenance.EnterCount);
        Assert.Equal(0, _backup.CreateCount);
        Assert.Empty(_docker.CreatedContainers);
        var persisted = await _stateStore.LoadOperationAsync(result.OperationId, CancellationToken.None);
        Assert.Equal(InstallOperationStates.Failed, persisted!.State);
    }

    [Fact]
    public async Task ArchiveHashMismatchMarksFailed()
    {
        var (transaction, operation) = await CreateTransactionAndOperationAsync(
            new FixedArchiveDownloader(writeMismatchedArchive: true));

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.Contains("SHA-256", result.LastError, StringComparison.Ordinal);
        var persisted = await _stateStore.LoadOperationAsync(result.OperationId, CancellationToken.None);
        Assert.Equal(InstallOperationStates.Failed, persisted!.State);
    }

    [Fact]
    public async Task ImageLoadFailureMarksFailedWithoutTouchingApp()
    {
        _docker.OnLoadImage = (_, _) => throw new UpdateOperationException(502, "加载失败");
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.False(result.MaintenanceEntered);
        Assert.Equal(0, _maintenance.EnterCount);
        Assert.Equal(0, _maintenance.CompleteCount);
        var persisted = await _stateStore.LoadOperationAsync(result.OperationId, CancellationToken.None);
        Assert.Equal(InstallOperationStates.Failed, persisted!.State);
    }

    [Fact]
    public async Task MaintenanceEntryFailureMarksFailed()
    {
        _maintenance.OnEnterMaintenance = new UpdateOperationException(502, "维护请求失败");
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.False(result.MaintenanceEntered);
    }

    [Fact]
    public async Task BackupFailureExitsMaintenanceAndMarksFailed()
    {
        _backup.OnCreateBackup = new UpdateOperationException(502, "备份失败");
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.Equal(1, _maintenance.EnterCount);
        Assert.Equal(1, _maintenance.CompleteCount);
        Assert.False(result.BackupCompleted);
    }

    [Fact]
    public async Task ReplaceFailureAfterBackupRestoresDatabaseAndOldContainer()
    {
        _docker.OnCreateContainer = new UpdateOperationException(502, "候选容器创建失败");
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.RolledBack, result.State);
        Assert.Equal(1, _backup.RestoreCount);
        Assert.Contains(
            (UpdateContractConstants.AppCurrentImageRepository, UpdateContractConstants.AppCurrentImageTagName),
            _docker.TaggedImages.Select(tag => (tag.Repository, tag.Tag)));
        Assert.Contains(_docker.StartedContainers, id => id == result.OldContainerId);
        Assert.Contains((result.CurrentVersion, false), _health.Calls);
        var persisted = await _stateStore.LoadOperationAsync(result.OperationId, CancellationToken.None);
        Assert.Equal(InstallOperationStates.RolledBack, persisted!.State);
    }

    [Fact]
    public async Task VerificationFailureRollsBack()
    {
        // 候选验证失败一次；回滚时旧版本验证成功。
        _health.FailTimes = 1;
        _health.FailureReason = "候选 App 不 ready";
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.RolledBack, result.State);
        Assert.Equal(1, _backup.RestoreCount);
        Assert.Contains((result.CurrentVersion, false), _health.Calls);
    }

    [Fact]
    public async Task CompleteMaintenanceFailureRollsBack()
    {
        _maintenance.OnCompleteMaintenance = new UpdateOperationException(502, "解除维护失败");
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.RolledBack, result.State);
        Assert.Equal(1, _backup.RestoreCount);
    }

    [Fact]
    public async Task RollbackFailureMarksFailedNeedsOperator()
    {
        _health.AlwaysFail = true;
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.FailedNeedsOperator, result.State);
        Assert.Contains("需要操作员介入", result.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RollbackRecreatesOldContainerWhenOriginalIsGone()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot();
        _docker.CreateContainerFailTimes = 1;
        await SeedReleaseCacheAsync();
        var operation = TestSnapshots.CreateOperation(backupCompleted: true, oldSnapshot: snapshot);
        // 替换阶段会以真实旧容器 ID 覆盖快照；模拟旧容器随后被删除（Updater 中断场景）。
        _docker.KnownContainerIds.Remove("app-container-1");
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);
        var transaction = CreateTransaction(new FixedArchiveDownloader());

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.RolledBack, result.State);
        // 原旧容器已不存在（不在 KnownContainerIds 中）→ 从快照重建。
        Assert.Single(_docker.CreatedContainers);
        Assert.Equal(snapshot.ContainerName, _docker.CreatedContainers[0].Contract.ContainerName);
        Assert.Equal(1, _backup.RestoreCount);
    }

    [Fact]
    public async Task MissingCachedReleaseMarksFailedWithPreflightReport()
    {
        var operation = TestSnapshots.CreateOperation();
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);
        var transaction = CreateTransaction(new FixedArchiveDownloader());

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.NotNull(result.PreflightReport);
        Assert.False(result.PreflightReport!.ReleaseCacheValid);
        var persisted = await _stateStore.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.True(persisted!.PreflightReport is { ReleaseCacheValid: false });
    }

    [Fact]
    public async Task InvalidCurrentContainerContractMarksFailed()
    {
        var invalid = TestSnapshots.CreateValidSnapshot() with { Mounts = [] };
        _docker.OnFindAppContainer = _ => Task.FromResult<AppContainerSnapshot?>(invalid);
        var (transaction, operation) = await CreateTransactionAndOperationAsync();

        var result = await transaction.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(InstallOperationStates.Failed, result.State);
        Assert.NotNull(result.PreflightReport);
        Assert.True(result.PreflightReport!.DockerReachable);
        Assert.Contains("数据卷", result.PreflightReport.Errors[0], StringComparison.Ordinal);
        var persisted = await _stateStore.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.NotNull(persisted!.PreflightReport);
        Assert.Contains("数据卷", persisted.PreflightReport!.Errors[0], StringComparison.Ordinal);
    }

    public void Dispose() => _temp.Dispose();

    private InstallTransactionService CreateTransaction(IDownloader downloader)
    {
        var rollback = new InstallRollbackService(
            _docker,
            _backup,
            _health,
            _stateStore,
            _options,
            TimeProvider.System);
        return new InstallTransactionService(
            _releaseCache,
            downloader,
            _docker,
            _maintenance,
            _backup,
            _health,
            rollback,
            _stateStore,
            _options,
            TimeProvider.System);
    }

    /// <summary>写入与下载器归档哈希一致的已验签 Release 缓存。</summary>
    private async Task<ReleaseManifest> SeedReleaseCacheAsync()
    {
        var manifest = new ReleaseManifestBuilder()
            .WithVersion(TestReleases.DefaultVersion)
            .WithOnlineInstallSupported(true)
            .WithManualUpgradeRequired(false)
            .WithAppArchiveSha256(TestSnapshots.ComputeSha256(ArchiveBytes))
            .WithAppSize(ArchiveBytes.Length)
            .Build();
        var manifestBytes = TestReleases.ToJson(manifest);
        var signature = TestKeys.Sign(_signingKey, manifestBytes);
        await _releaseCache.SaveAsync(manifest, manifestBytes, signature, CancellationToken.None);
        return manifest;
    }

    /// <summary>写入已验签 Release 缓存，返回事务与预置操作。</summary>
    private async Task<(InstallTransactionService Transaction, InstallOperationRecord Operation)>
        CreateTransactionAndOperationAsync(IDownloader? downloader = null)
    {
        var actualDownloader = downloader ?? new FixedArchiveDownloader();
        var transaction = CreateTransaction(actualDownloader);
        var manifest = await SeedReleaseCacheAsync();

        var operation = TestSnapshots.CreateOperation(targetVersion: manifest.Version);
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);
        return (transaction, operation);
    }

    /// <summary>下载器替身：写入固定归档字节（哈希与测试 manifest 一致，除非故意不匹配）。</summary>
    private sealed class FixedArchiveDownloader : IDownloader
    {
        public FixedArchiveDownloader(bool throwOnDownload = false, bool writeMismatchedArchive = false)
        {
            ThrowOnDownload = throwOnDownload;
            WriteMismatchedArchive = writeMismatchedArchive;
        }

        public bool ThrowOnDownload { get; }

        public bool WriteMismatchedArchive { get; }

        public Task<DownloadResult> DownloadAsync(
            string url,
            string destinationPath,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            if (ThrowOnDownload)
            {
                throw new UpdateOperationException(StatusCodes.Status502BadGateway, "下载失败。");
            }

            byte[] bytes;
            if (WriteMismatchedArchive)
            {
                bytes = (byte[])ArchiveBytes.Clone();
                bytes[^1] ^= 0xFF;
            }
            else
            {
                bytes = ArchiveBytes;
            }
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(destinationPath, bytes);
            return Task.FromResult(new DownloadResult(
                destinationPath,
                bytes.Length,
                TestSnapshots.ComputeSha256(bytes)));
        }
    }
}
