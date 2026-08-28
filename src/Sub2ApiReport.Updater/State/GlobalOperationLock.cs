namespace Sub2ApiReport.Updater.State;

/// <summary>
/// 全局升级操作锁：进程内信号量 + 状态目录内独占锁文件，保证同一时刻只有一个检查/安装事务。
/// </summary>
public sealed class GlobalOperationLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);
    private readonly string _lockFilePath;

    public GlobalOperationLock(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _lockFilePath = Path.Combine(stateDirectory, "operations.lock");
    }

    public async Task<OperationLockScope> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64,
                FileOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _semaphore.Release();
            throw new UpdateOperationException(
                StatusCodes.Status409Conflict,
                "另一个升级操作正在进行中。",
                exception);
        }

        return new OperationLockScope(_semaphore, lockFile);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    public sealed class OperationLockScope(SemaphoreSlim semaphore, FileStream lockFile) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await lockFile.DisposeAsync();
            semaphore.Release();
        }
    }
}
