using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Install;

/// <summary>启动恢复：将上次进程中断遗留的非终态操作安全收敛为终态。</summary>
public interface IInstallRecovery
{
    Task RecoverAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 恢复规则：备份已完成的非终态操作执行完整回滚（数据库/镜像/容器），成功记 RolledBack、
/// 失败记 FailedNeedsOperator；备份未完成的非终态操作直接记 FailedNeedsOperator
/// （先尽力退出维护模式，避免 App 卡在维护状态）。
/// </summary>
public sealed class InstallRecovery(
    UpdateStateStore stateStore,
    IAppMaintenanceClient maintenanceClient,
    InstallRollbackService rollbackService,
    TimeProvider timeProvider) : IInstallRecovery
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var operations = (await stateStore.LoadAllOperationsAsync(cancellationToken))
            .Where(operation => !InstallOperationStates.IsTerminal(operation.State))
            .OrderBy(operation => operation.CreatedAt)
            .ToList();

        foreach (var original in operations)
        {
            if (original.BackupCompleted)
            {
                await rollbackService.RollbackAsync(
                    original,
                    "Updater 中断后启动恢复：执行回滚。",
                    cancellationToken);
                continue;
            }

            var message = "Updater 中断后启动恢复：升级未完成且备份不可用，需要操作员介入。";
            var operation = original;
            if (operation.MaintenanceEntered)
            {
                try
                {
                    await maintenanceClient.CompleteMaintenanceAsync(
                        operation.OperationId,
                        cancellationToken);
                    operation = operation with { MaintenanceEntered = false };
                }
                catch (Exception exception)
                {
                    message = $"{message}（退出维护模式失败：{exception.Message}）";
                }
            }

            operation = operation with
            {
                State = InstallOperationStates.FailedNeedsOperator,
                CompletedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow(),
                LastError = message,
            };
            await stateStore.SaveOperationAsync(operation, cancellationToken);
        }
    }
}

/// <summary>托管启动顺序：恢复在任何安装事务开始前完成。</summary>
public sealed class InstallRecoveryHostedService(IInstallRecovery recovery) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await recovery.RecoverAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 停机时未完成的恢复由下次启动重试。
        }
    }
}
