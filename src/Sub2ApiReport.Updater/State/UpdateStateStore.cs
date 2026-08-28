using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.Updater.State;

/// <summary>持久化的最近一次检查结果快照。不包含凭证、报告内容或 GitHub token。</summary>
public sealed record UpdateStatusSnapshot(
    DateTimeOffset? LastCheckedAt,
    bool UpdateAvailable,
    string? AvailableVersion,
    DateTimeOffset? AvailablePublishedAt,
    bool ManualUpgradeRequired,
    string? CurrentVersion,
    string? LastError);

/// <summary>
/// 升级状态与缓存存储。写入采用“临时文件 + flush-to-disk + 原子改名”，保证断电/中止后不残留半写状态。
/// </summary>
public sealed class UpdateStateStore
{
    private const string StatusFileName = "status.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _stateDirectory;
    private readonly string _statusFilePath;

    public UpdateStateStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _stateDirectory = stateDirectory;
        Directory.CreateDirectory(stateDirectory);
        _statusFilePath = Path.Combine(stateDirectory, StatusFileName);
    }

    public string GetDownloadsDirectory()
    {
        var directory = Path.Combine(_stateDirectory, "downloads");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string GetBackupsDirectory()
    {
        var directory = Path.Combine(_stateDirectory, "backups");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private string GetOperationsDirectory()
    {
        var directory = Path.Combine(_stateDirectory, "operations");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public async Task SaveOperationAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, SerializerOptions);
        var filePath = Path.Combine(GetOperationsDirectory(), $"{record.OperationId}.json");
        await WriteAtomicAsync(filePath, bytes, cancellationToken);
    }

    public async Task DeleteOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        try
        {
            File.Delete(Path.Combine(GetOperationsDirectory(), $"{operationId}.json"));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
        }

        await Task.CompletedTask;
    }

    public async Task<InstallOperationRecord?> LoadOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var filePath = Path.Combine(GetOperationsDirectory(), $"{operationId}.json");
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallOperationRecord>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<InstallOperationRecord>> LoadAllOperationsAsync(CancellationToken cancellationToken)
    {
        var directory = GetOperationsDirectory();
        var records = new List<InstallOperationRecord>();
        foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(filePath => filePath, StringComparer.Ordinal))
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<InstallOperationRecord>(bytes, SerializerOptions) is { } record)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // 损坏的历史操作文件不阻断启动恢复。
            }
        }

        return records;
    }

    public async Task SaveReleaseCacheAsync(ReleaseCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var directory = Path.Combine(_stateDirectory, "cache");
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions);
        await WriteAtomicAsync(Path.Combine(directory, "release-cache.json"), bytes, cancellationToken);
    }

    public async Task<ReleaseCacheEntry?> LoadReleaseCacheAsync(CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_stateDirectory, "cache", "release-cache.json");
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleaseCacheEntry>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteAtomicAsync(string filePath, byte[] bytes, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(filePath) ?? _stateDirectory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task SaveStatusAsync(UpdateStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
        await WriteAtomicAsync(_statusFilePath, bytes, cancellationToken);
    }

    public async Task<UpdateStatusSnapshot?> LoadStatusAsync(CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_statusFilePath, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateStatusSnapshot>(bytes, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
