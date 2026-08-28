using System.Threading.Channels;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Install;

/// <summary>
/// 有界单项目安装队列：同一时刻最多一个排队/运行中的安装操作，
/// 忙碌时拒绝新的安装请求（由调用方返回 409）。
/// </summary>
public interface IInstallCoordinator
{
    bool IsBusy { get; }

    bool TryEnqueue(string operationId);
}

/// <summary>
/// BackgroundService 实现。进程停止会中断运行中的事务；非终态操作快照保留在磁盘上，
/// 由启动恢复逻辑在下次启动时回滚或标记 FailedNeedsOperator。
/// </summary>
public sealed class InstallQueueService(IInstallTransaction transaction, UpdateStateStore stateStore)
    : BackgroundService, IInstallCoordinator
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(capacity: 1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private readonly object _gate = new();
    private bool _busy;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _busy;
            }
        }
    }

    public bool TryEnqueue(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        lock (_gate)
        {
            if (_busy)
            {
                return false;
            }

            if (!_channel.Writer.TryWrite(operationId))
            {
                return false;
            }

            _busy = true;
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_channel.Reader.TryRead(out var operationId))
                {
                    try
                    {
                        var operation = await stateStore.LoadOperationAsync(operationId, stoppingToken);
                        if (operation is not null
                            && !InstallOperationStates.IsTerminal(operation.State))
                        {
                            await transaction.ExecuteAsync(operation, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // 事务内部负责持久化终态；这里兜底吞掉意外异常避免队列终止。
                    }
                }

                lock (_gate)
                {
                    _busy = false;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 进程停止：非终态操作交由启动恢复处理。
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }
}
