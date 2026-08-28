using System.Text.Json;
using System.Text.Json.Serialization;

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

    public async Task SaveStatusAsync(UpdateStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var tempPath = Path.Combine(_stateDirectory, $".{StatusFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
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

            File.Move(tempPath, _statusFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
