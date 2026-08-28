using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.Install;

public sealed class SqliteBackupServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _databasePath;
    private readonly UpdateStateStore _stateStore;
    private readonly UpdateOptions _options;
    private readonly SqliteBackupService _service;

    public SqliteBackupServiceTests()
    {
        _databasePath = Path.Combine(_temp.FullPath, "db", "sub2api-report.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _stateStore = new UpdateStateStore(Path.Combine(_temp.FullPath, "state"));
        _options = new UpdateOptions { DatabasePath = _databasePath };
        _service = new SqliteBackupService(_options, _stateStore);
    }

    [Fact]
    public async Task CreateBackupProducesVerifiedSnapshotWithIntegrityCheckAndHash()
    {
        await CreateSeededDatabaseAsync("report-1");
        var operationId = Guid.NewGuid().ToString("N");

        var result = await _service.CreateBackupAsync(operationId, CancellationToken.None);

        Assert.True(File.Exists(result.FilePath));
        Assert.EndsWith(Path.Combine("state", "backups", $"{operationId}.db"), result.FilePath);
        Assert.Equal(new FileInfo(result.FilePath).Length, result.Size);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(result.FilePath))).ToLowerInvariant(),
            result.Sha256);
    }

    [Fact]
    public async Task RestoreBackupReplacesDatabaseContentAndRemovesWalShm()
    {
        await CreateSeededDatabaseAsync("report-1");
        var backup = await _service.CreateBackupAsync("op-1", CancellationToken.None);

        // 模拟候选版本写入新数据并留下 WAL/SHM。
        await using (var connection = OpenDatabase())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM reports; INSERT INTO reports(Name) VALUES('candidate-only');";
            await command.ExecuteNonQueryAsync();
        }

        await File.WriteAllTextAsync(_databasePath + "-wal", "wal");
        await File.WriteAllTextAsync(_databasePath + "-shm", "shm");

        await _service.RestoreBackupAsync("op-1", backup.FilePath, backup.Sha256, CancellationToken.None);

        Assert.False(File.Exists(_databasePath + "-wal"));
        Assert.False(File.Exists(_databasePath + "-shm"));
        await using var restored = OpenDatabase();
        await restored.OpenAsync();
        var read = restored.CreateCommand();
        read.CommandText = "SELECT Name FROM reports ORDER BY Name;";
        using var reader = await read.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Equal(["report-1"], names);
    }

    [Fact]
    public async Task RestoreRejectsTamperedBackup()
    {
        await CreateSeededDatabaseAsync("report-1");
        var result = await _service.CreateBackupAsync("op-1", CancellationToken.None);
        await File.WriteAllTextAsync(result.FilePath, "tampered content");

        await Assert.ThrowsAsync<UpdateOperationException>(() => _service.RestoreBackupAsync(
            "op-1",
            result.FilePath,
            result.Sha256,
            CancellationToken.None));
    }

    [Fact]
    public async Task RestoreRejectsMissingBackupFile()
    {
        await Assert.ThrowsAsync<UpdateOperationException>(() => _service.RestoreBackupAsync(
            "op-1",
            Path.Combine(_temp.FullPath, "missing.db"),
            TestSnapshots.ComputeSha256([1, 2, 3]),
            CancellationToken.None));
    }

    [Fact]
    public async Task BackupRejectsCorruptedDatabaseWithIntegrityFailure()
    {
        // 非数据库文件：Backup API 失败或 integrity_check 不通过都应拒绝。
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await File.WriteAllTextAsync(_databasePath, "this is not a sqlite database at all");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.CreateBackupAsync("op-1", CancellationToken.None));
    }

    [Fact]
    public async Task BackupOfEmptyDatabasePassesIntegrityCheck()
    {
        await using (var connection = OpenDatabase())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE reports(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var result = await _service.CreateBackupAsync("op-1", CancellationToken.None);

        Assert.True(File.Exists(result.FilePath));
        Assert.NotEqual(0, result.Size);
    }

    public void Dispose() => _temp.Dispose();

    private SqliteConnection OpenDatabase() =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

    private async Task CreateSeededDatabaseAsync(string reportName)
    {
        await using var connection = OpenDatabase();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS reports(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"INSERT INTO reports(Name) VALUES('{reportName}');";
        await command.ExecuteNonQueryAsync();
    }
}
