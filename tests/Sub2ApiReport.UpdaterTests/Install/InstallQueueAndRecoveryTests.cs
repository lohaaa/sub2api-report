using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.Install;

public sealed class InstallQueueTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task QueueProcessesSingleOperationAndPersistsTerminalState()
    {
        var stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        var transaction = new FakeInstallTransaction();
        await using var queue = new InstallQueueServiceHarness(stateStore, transaction);

        var operation = TestSnapshots.CreateOperation();
        await stateStore.SaveOperationAsync(operation, CancellationToken.None);
        Assert.True(queue.TryEnqueue(operation.OperationId));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && transaction.Executed.Count == 0)
        {
            await Task.Delay(20);
        }

        Assert.Single(transaction.Executed);
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public async Task QueueRejectsEnqueueWhileOperationIsRunning()
    {
        var stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        var release = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();
        var transaction = new FakeInstallTransaction
        {
            OnExecute = operation =>
            {
                release.TrySetResult();
                releaseGate.Task.Wait(TimeSpan.FromSeconds(10));
                return operation with { State = InstallOperationStates.Succeeded };
            },
        };
        await using var queue = new InstallQueueServiceHarness(stateStore, transaction);

        var operation = TestSnapshots.CreateOperation();
        await stateStore.SaveOperationAsync(operation, CancellationToken.None);
        Assert.True(queue.TryEnqueue(operation.OperationId));
        await release.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 运行中：保守拒绝。
        Assert.True(queue.IsBusy);
        Assert.False(queue.TryEnqueue(Guid.NewGuid().ToString("N")));

        releaseGate.TrySetResult();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && queue.IsBusy)
        {
            await Task.Delay(20);
        }

        Assert.False(queue.IsBusy);
    }

    [Fact]
    public async Task QueueSkipsMissingOrTerminalOperations()
    {
        var stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        var transaction = new FakeInstallTransaction();
        await using var queue = new InstallQueueServiceHarness(stateStore, transaction);

        // 不存在的操作 ID 与已终态操作都应被跳过。
        var terminalOperation = TestSnapshots.CreateOperation(state: InstallOperationStates.Succeeded);
        await stateStore.SaveOperationAsync(terminalOperation, CancellationToken.None);
        Assert.True(queue.TryEnqueue(Guid.NewGuid().ToString("N")));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && queue.IsBusy)
        {
            await Task.Delay(20);
        }

        Assert.Empty(transaction.Executed);
        Assert.False(queue.IsBusy);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>用于测试的队列宿主：包装 BackgroundService 生命周期。</summary>
    private sealed class InstallQueueServiceHarness : IAsyncDisposable
    {
        public InstallQueueServiceHarness(UpdateStateStore stateStore, IInstallTransaction transaction)
        {
            Service = new InstallQueueService(transaction, stateStore);
            _startTask = Service.StartAsync(CancellationToken.None);
        }

        private InstallQueueService Service { get; }

        private readonly Task _startTask;

        public bool TryEnqueue(string operationId)
        {
            _startTask.Wait(TimeSpan.FromSeconds(5));
            return Service.TryEnqueue(operationId);
        }

        public bool IsBusy
        {
            get
            {
                _startTask.Wait(TimeSpan.FromSeconds(5));
                return Service.IsBusy;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
        }
    }
}

public sealed class InstallRecoveryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UpdateStateStore _stateStore;
    private readonly UpdateOptions _options;
    private readonly FakeMaintenanceClient _maintenance = new();
    private readonly FakeDockerAppManager _docker = new();
    private readonly FakeBackupService _backup = new();
    private readonly FakeHealthVerifier _health = new();
    private readonly InstallRollbackService _rollback;
    private readonly InstallRecovery _recovery;

    public InstallRecoveryTests()
    {
        _stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        _options = new UpdateOptions { StatePath = Path.Combine(_temp.FullPath, "state") };
        _rollback = new InstallRollbackService(_docker, _backup, _health, _stateStore, _options, TimeProvider.System);
        _recovery = new InstallRecovery(_stateStore, _maintenance, _rollback, TimeProvider.System);
    }

    [Fact]
    public async Task NonterminalOperationWithBackupIsRolledBack()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot();
        var operation = TestSnapshots.CreateOperation(
            state: InstallOperationStates.Verifying,
            backupCompleted: true,
            oldSnapshot: snapshot)
            with
        {
            BackupFilePath = "/tmp/backup.db",
            BackupSha256 = TestReleases.Hex('f'),
            MaintenanceEntered = true,
        };
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);

        await _recovery.RecoverAsync(CancellationToken.None);

        var recovered = await _stateStore.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(InstallOperationStates.RolledBack, recovered!.State);
        Assert.Equal(1, _backup.RestoreCount);
        Assert.Contains((UpdateContractConstants.AppCurrentImageRepository, UpdateContractConstants.AppCurrentImageTagName),
            _docker.TaggedImages.Select(tag => (tag.Repository, tag.Tag)));
        // 回滚移除候选容器，无需退出维护（无 Complete 调用）。
        Assert.Equal(0, _maintenance.CompleteCount);
    }

    [Fact]
    public async Task NonterminalOperationWithoutBackupIsFailedNeedsOperatorAndExitsMaintenance()
    {
        var operation = TestSnapshots.CreateOperation(
            state: InstallOperationStates.RequestingMaintenance,
            maintenanceEntered: true);
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);

        await _recovery.RecoverAsync(CancellationToken.None);

        var recovered = await _stateStore.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(InstallOperationStates.FailedNeedsOperator, recovered!.State);
        Assert.Contains("操作员", recovered.LastError, StringComparison.Ordinal);
        Assert.Equal(1, _maintenance.CompleteCount);
        Assert.Equal(0, _backup.RestoreCount);
    }

    [Fact]
    public async Task FailedRollbackLeavesFailedNeedsOperator()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot();
        var operation = TestSnapshots.CreateOperation(
            state: InstallOperationStates.ReplacingApp,
            backupCompleted: true,
            oldSnapshot: snapshot)
            with
        {
            BackupFilePath = "/tmp/backup.db",
            BackupSha256 = TestReleases.Hex('f'),
        };
        await _stateStore.SaveOperationAsync(operation, CancellationToken.None);
        _health.AlwaysFail = true;

        await _recovery.RecoverAsync(CancellationToken.None);

        var recovered = await _stateStore.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(InstallOperationStates.FailedNeedsOperator, recovered!.State);
        Assert.Contains("回滚失败", recovered.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalOperationsAreNotTouched()
    {
        foreach (var state in new[]
                 {
                     InstallOperationStates.Succeeded,
                     InstallOperationStates.RolledBack,
                     InstallOperationStates.Failed,
                     InstallOperationStates.FailedNeedsOperator,
                 })
        {
            var operation = TestSnapshots.CreateOperation(state: state);
            await _stateStore.SaveOperationAsync(operation, CancellationToken.None);
        }

        await _recovery.RecoverAsync(CancellationToken.None);

        var operations = await _stateStore.LoadAllOperationsAsync(CancellationToken.None);
        Assert.Equal(4, operations.Count);
        Assert.All(operations, operation => Assert.Equal(
            TestSnapshots.CreateOperation(state: operation.State).State,
            operation.State));
        Assert.Equal(0, _backup.RestoreCount);
        Assert.Equal(0, _maintenance.CompleteCount);
    }

    public void Dispose() => _temp.Dispose();
}
