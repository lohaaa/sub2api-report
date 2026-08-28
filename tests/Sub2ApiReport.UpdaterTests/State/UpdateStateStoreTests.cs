using System.Text.Json;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.State;

public sealed class UpdateStateStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UpdateStateStore _store;

    public UpdateStateStoreTests()
    {
        _store = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
    }

    [Fact]
    public async Task SaveThenLoadRoundtripsSnapshot()
    {
        var snapshot = new UpdateStatusSnapshot(
            LastCheckedAt: new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero),
            UpdateAvailable: true,
            AvailableVersion: "1.2.0",
            AvailablePublishedAt: TestReleases.PublishedAt,
            ManualUpgradeRequired: true,
            CurrentVersion: "0.7.0",
            LastError: null);

        await _store.SaveStatusAsync(snapshot, CancellationToken.None);

        Assert.Equal(snapshot, await _store.LoadStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveReplacesPreviousSnapshotWithoutTempResidue()
    {
        var first = new UpdateStatusSnapshot(
            DateTimeOffset.UtcNow, false, null, null, false, "0.7.0", null);
        var second = new UpdateStatusSnapshot(
            DateTimeOffset.UtcNow, true, "1.2.0", TestReleases.PublishedAt, false, "0.7.0", null);

        await _store.SaveStatusAsync(first, CancellationToken.None);
        await _store.SaveStatusAsync(second, CancellationToken.None);

        var stateDirectory = Path.Combine(_temp.FullPath, "state");
        var leftovers = Directory.EnumerateFiles(stateDirectory, "*.tmp", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(stateDirectory, ".*", SearchOption.AllDirectories))
            .ToList();
        Assert.Empty(leftovers);
        Assert.Equal(
            [Path.Combine(stateDirectory, "status.json")],
            Directory.EnumerateFiles(stateDirectory, "*", SearchOption.TopDirectoryOnly).OrderBy(f => f));
        Assert.Equal(
            second,
            await _store.LoadStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptStatusFileLoadsAsNull()
    {
        var stateDirectory = Path.Combine(_temp.FullPath, "state");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "status.json"),
            "{ not valid json");

        Assert.Null(await _store.LoadStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingStatusFileLoadsAsNull()
    {
        Assert.Null(await _store.LoadStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnknownStatusFieldsAreRejectedOnLoad()
    {
        var stateDirectory = Path.Combine(_temp.FullPath, "state");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "status.json"),
            """{"last_checked_at":null,"update_available":false,"sneaky":true}""");

        Assert.Null(await _store.LoadStatusAsync(CancellationToken.None));
    }

    public void Dispose() => _temp.Dispose();
}

public sealed class GlobalOperationLockTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task LockIsExclusiveWithinProcess()
    {
        using var operationLock = new GlobalOperationLock(_temp.FullPath);
        var scope = await operationLock.AcquireAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => operationLock.AcquireAsync(timeout.Token));
        }
        finally
        {
            await scope.DisposeAsync();
        }

        await using var reacquired = await operationLock.AcquireAsync(CancellationToken.None);
        Assert.NotNull(reacquired);
    }

    [Fact]
    public async Task LockFileBlocksOtherProcesses()
    {
        using var operationLock = new GlobalOperationLock(_temp.FullPath);
        var scope = await operationLock.AcquireAsync(CancellationToken.None);
        try
        {
            Assert.ThrowsAny<IOException>(() => new FileStream(
                Path.Combine(_temp.FullPath, "operations.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64));
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReleasedLockFileIsReopenable()
    {
        using var operationLock = new GlobalOperationLock(_temp.FullPath);
        var scope = await operationLock.AcquireAsync(CancellationToken.None);
        await scope.DisposeAsync();

        await using var reopened = await operationLock.AcquireAsync(CancellationToken.None);
        Assert.NotNull(reopened);
    }

    public void Dispose() => _temp.Dispose();
}
