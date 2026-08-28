using System.Text.Json;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.State;

public sealed class InstallStatePersistenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UpdateStateStore _store;

    public InstallStatePersistenceTests()
    {
        _store = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
    }

    [Fact]
    public async Task OperationRecordRoundtripsWithContainerSnapshot()
    {
        var operation = TestSnapshots.CreateOperation(
            state: InstallOperationStates.ReplacingApp,
            backupCompleted: true,
            oldSnapshot: TestSnapshots.CreateValidSnapshot())
            with
        {
            BackupFilePath = "/tmp/backup.db",
            BackupSha256 = TestReleases.Hex('f'),
            PreflightReport = new InstallPreflightReport(
                true, true, true, true, true, ["warn"]),
        };

        await _store.SaveOperationAsync(operation, CancellationToken.None);

        var loaded = await _store.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(operation.OperationId, loaded!.OperationId);
        Assert.Equal(operation.State, loaded.State);
        Assert.True(loaded.BackupCompleted);
        Assert.Equal(operation.BackupFilePath, loaded.BackupFilePath);
        Assert.Equal(operation.BackupSha256, loaded.BackupSha256);
        Assert.Equal(operation.LoadedImageId, loaded.LoadedImageId);
        Assert.Equal(operation.Stages.Select(stage => (stage.Stage, stage.CompletedAt)), loaded.Stages.Select(stage => (stage.Stage, stage.CompletedAt)));

        var snapshot = loaded.OldContainerSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal(operation.OldContainerSnapshot!.ContainerId, snapshot!.ContainerId);
        Assert.Equal(operation.OldContainerSnapshot.ContainerName, snapshot.ContainerName);
        Assert.Equal(operation.OldContainerSnapshot.ImageId, snapshot.ImageId);
        Assert.Equal(operation.OldContainerSnapshot.Env, snapshot.Env);
        Assert.Equal(operation.OldContainerSnapshot.Labels, snapshot.Labels);
        Assert.Equal(operation.OldContainerSnapshot.Mounts, snapshot.Mounts);
        Assert.Equal(
            operation.OldContainerSnapshot.Networks.Select(network => (network.Network, string.Join(',', network.Aliases))),
            snapshot.Networks.Select(network => (network.Network, string.Join(',', network.Aliases))));
        Assert.Equal(operation.OldContainerSnapshot.PortBindings, snapshot.PortBindings);
        Assert.Equal(operation.OldContainerSnapshot.RestartPolicy, snapshot.RestartPolicy);
        Assert.Equal(operation.OldContainerSnapshot.SecurityOptions, snapshot.SecurityOptions);
        Assert.Equal(operation.OldContainerSnapshot.Resources, snapshot.Resources);
        Assert.Equal(operation.OldContainerSnapshot.Healthcheck!.Test, snapshot.Healthcheck!.Test);
        Assert.Equal(operation.OldContainerSnapshot.Healthcheck.Interval, snapshot.Healthcheck.Interval);
        Assert.Equal(operation.OldContainerSnapshot.Healthcheck.Timeout, snapshot.Healthcheck.Timeout);
        Assert.Equal(operation.OldContainerSnapshot.Healthcheck.StartPeriod, snapshot.Healthcheck.StartPeriod);
        Assert.Equal(operation.OldContainerSnapshot.Healthcheck.Retries, snapshot.Healthcheck.Retries);

        var report = loaded.PreflightReport;
        Assert.NotNull(report);
        Assert.True(report!.ReleaseCacheValid);
        Assert.True(report.DockerReachable);
        Assert.True(report.CurrentAppReady);
        Assert.True(report.DatabaseAccessible);
        Assert.True(report.StateDirectoryWritable);
        Assert.Equal(["warn"], report.Errors);
    }

    [Fact]
    public async Task LoadOperationReturnsNullForMissingOrCorruptFile()
    {
        Assert.Null(await _store.LoadOperationAsync("missing", CancellationToken.None));

        var directory = Path.Combine(_temp.FullPath, "state", "operations");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{ not valid");

        Assert.Null(await _store.LoadOperationAsync("broken", CancellationToken.None));
    }

    [Fact]
    public async Task LoadAllOperationsSkipsCorruptFilesAndOrdersByName()
    {
        var directory = Path.Combine(_temp.FullPath, "state", "operations");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{ not valid");
        var first = TestSnapshots.CreateOperation();
        var second = TestSnapshots.CreateOperation();
        await _store.SaveOperationAsync(first, CancellationToken.None);
        await _store.SaveOperationAsync(second, CancellationToken.None);

        var operations = await _store.LoadAllOperationsAsync(CancellationToken.None);

        Assert.Equal(2, operations.Count);
        Assert.Contains(first.OperationId, operations.Select(operation => operation.OperationId));
        Assert.Contains(second.OperationId, operations.Select(operation => operation.OperationId));
    }

    [Fact]
    public async Task DeleteOperationRemovesFile()
    {
        var operation = TestSnapshots.CreateOperation();
        await _store.SaveOperationAsync(operation, CancellationToken.None);

        await _store.DeleteOperationAsync(operation.OperationId, CancellationToken.None);

        Assert.Null(await _store.LoadOperationAsync(operation.OperationId, CancellationToken.None));
    }

    [Fact]
    public async Task OperationRejectsUnknownFieldsOnLoad()
    {
        var operation = TestSnapshots.CreateOperation();
        await _store.SaveOperationAsync(operation, CancellationToken.None);
        var filePath = Path.Combine(_temp.FullPath, "state", "operations", $"{operation.OperationId}.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(filePath))!;
        json["sneaky"] = 1;
        await File.WriteAllTextAsync(filePath, json.ToJsonString());

        Assert.Null(await _store.LoadOperationAsync(operation.OperationId, CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseCacheRoundtrips()
    {
        var entry = new ReleaseCacheEntry(
            DateTimeOffset.UtcNow,
            """{"schemaVersion":1,"version":"1.2.0"}""",
            Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        await _store.SaveReleaseCacheAsync(entry, CancellationToken.None);

        Assert.Equal(entry, await _store.LoadReleaseCacheAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseCacheMissingOrCorruptLoadsAsNull()
    {
        Assert.Null(await _store.LoadReleaseCacheAsync(CancellationToken.None));

        var directory = Path.Combine(_temp.FullPath, "state", "cache");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "release-cache.json"), "{ broken");

        Assert.Null(await _store.LoadReleaseCacheAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AtomicWriteLeavesNoTempFiles()
    {
        var operation = TestSnapshots.CreateOperation();
        await _store.SaveOperationAsync(operation, CancellationToken.None);
        await _store.SaveOperationAsync(operation with { State = InstallOperationStates.Succeeded }, CancellationToken.None);

        var stateDirectory = Path.Combine(_temp.FullPath, "state");
        var leftovers = Directory.EnumerateFiles(stateDirectory, ".*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(stateDirectory, "*.tmp", SearchOption.AllDirectories))
            .ToList();
        Assert.Empty(leftovers);
        var loaded = await _store.LoadOperationAsync(operation.OperationId, CancellationToken.None);
        Assert.Equal(InstallOperationStates.Succeeded, loaded!.State);
    }

    public void Dispose() => _temp.Dispose();
}
