using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Backup;

public sealed record SqliteBackupResult(string FilePath, long Size, string Sha256);

/// <summary>
/// SQLite 备份与恢复接口。备份使用 SQLite Backup API（在线一致性快照），恢复为安全的
/// 文件级整库替换（App 容器已停止时执行）。测试使用注入的替身。
/// </summary>
public interface ISqliteBackupService
{
    Task<SqliteBackupResult> CreateBackupAsync(string operationId, CancellationToken cancellationToken);

    Task RestoreBackupAsync(
        string operationId,
        string backupFilePath,
        string expectedSha256,
        CancellationToken cancellationToken);
}

/// <summary>
/// 基于 Microsoft.Data.Sqlite 的生产实现：
/// 备份：BackupDatabase 写入临时文件 → integrity_check → SHA-256 → flush-to-disk → 原子改名；
/// 恢复：校验备份哈希 → 原子替换数据库文件 → 清理属于旧数据库的 -wal/-shm 文件。
/// </summary>
public sealed class SqliteBackupService(UpdateOptions options, UpdateStateStore stateStore) : ISqliteBackupService
{
    private const string IntegrityCheckOk = "ok";

    public async Task<SqliteBackupResult> CreateBackupAsync(string operationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var backupsDirectory = stateStore.GetBackupsDirectory();
        var tempPath = Path.Combine(backupsDirectory, $"{operationId}.db.tmp");
        var finalPath = Path.Combine(backupsDirectory, $"{operationId}.db");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = tempPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        try
        {
            await using var source = new SqliteConnection(sourceConnectionString);
            await source.OpenAsync(cancellationToken);
            await using var destination = new SqliteConnection(destinationConnectionString);
            await destination.OpenAsync(cancellationToken);

            // 在线备份：得到与源库事务一致的整体快照（WAL 模式下同样安全）。
            source.BackupDatabase(destination);

            await using (var integrityCheck = destination.CreateCommand())
            {
                integrityCheck.CommandText = "PRAGMA integrity_check;";
                var result = await integrityCheck.ExecuteScalarAsync(cancellationToken) as string;
                if (!string.Equals(result, IntegrityCheckOk, StringComparison.Ordinal))
                {
                    throw new UpdateOperationException(
                        StatusCodes.Status502BadGateway,
                        "升级备份完整性检查失败。");
                }
            }

            await destination.CloseAsync();
            await source.CloseAsync();

            var sha256 = await ComputeSha256Async(tempPath, cancellationToken);
            var size = new FileInfo(tempPath).Length;
            File.Move(tempPath, finalPath, overwrite: true);
            return new SqliteBackupResult(finalPath, size, sha256);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task RestoreBackupAsync(
        string operationId,
        string backupFilePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (!File.Exists(backupFilePath))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "升级备份文件缺失，无法回滚数据库。");
        }

        var actualSha256 = await ComputeSha256Async(backupFilePath, cancellationToken);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "升级备份哈希校验失败，拒绝恢复数据库。");
        }

        var databasePath = options.DatabasePath;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 先写到同目录临时文件再原子改名，避免半写数据库；旧库的 -wal/-shm 必须清理。
        var tempPath = $"{databasePath}.restore-{operationId}.tmp";
        try
        {
            File.Copy(backupFilePath, tempPath, overwrite: true);
            await FlushFileToDiskAsync(tempPath, cancellationToken);
            File.Move(tempPath, databasePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        foreach (var sidecarSuffix in new[] { "-wal", "-shm" })
        {
            var sidecarPath = databasePath + sidecarSuffix;
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }
        }

        await Task.CompletedTask;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task FlushFileToDiskAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }
}
