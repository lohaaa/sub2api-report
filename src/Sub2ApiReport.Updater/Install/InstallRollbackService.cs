using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Install;

/// <summary>
/// 回滚服务：停止并移除候选容器 → 恢复升级前数据库备份 → 恢复旧镜像 current 标签 →
/// 重建/启动旧容器 → 验证旧版本 readiness。成功写 RolledBack；回滚本身失败写 FailedNeedsOperator。
/// 由安装事务（安装后阶段失败）与启动恢复共用。
/// </summary>
public sealed class InstallRollbackService(
    IDockerAppManager dockerAppManager,
    ISqliteBackupService backupService,
    IHealthVerifier healthVerifier,
    UpdateStateStore stateStore,
    UpdateOptions options,
    TimeProvider timeProvider)
{
    public async Task<InstallOperationRecord> RollbackAsync(
        InstallOperationRecord record,
        string failureReason,
        CancellationToken cancellationToken)
    {
        record = await TransitionAsync(record, InstallOperationStates.RollingBack, failureReason, cancellationToken);
        try
        {
            await RemoveCandidateContainerAsync(record, cancellationToken);
            await RestoreDatabaseAsync(record, cancellationToken);
            await RestoreCurrentImageTagAsync(record, cancellationToken);
            await RestoreOldContainerAsync(record, cancellationToken);
            await VerifyOldAppAsync(record, cancellationToken);

            record = record with
            {
                State = InstallOperationStates.RolledBack,
                CompletedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow(),
                LastError = failureReason,
            };
            await stateStore.SaveOperationAsync(record, cancellationToken);
            return record;
        }
        catch (Exception rollbackException)
        {
            record = record with
            {
                State = InstallOperationStates.FailedNeedsOperator,
                CompletedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow(),
                LastError = $"回滚失败，需要操作员介入：{rollbackException.Message}（原始失败：{failureReason}）",
            };
            await stateStore.SaveOperationAsync(record, cancellationToken);
            return record;
        }
    }

    private async Task RemoveCandidateContainerAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        var candidateId = record.CandidateContainerId;
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            var candidate = await dockerAppManager.FindContainerByOperationAsync(record.OperationId, cancellationToken);
            candidateId = candidate?.ContainerId;
        }

        if (string.IsNullOrWhiteSpace(candidateId))
        {
            return;
        }

        await dockerAppManager.StopContainerAsync(
            candidateId,
            options.ContainerStopWaitSeconds,
            cancellationToken);
        await dockerAppManager.RemoveContainerAsync(candidateId, cancellationToken);
    }

    private async Task RestoreDatabaseAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        if (!record.BackupCompleted)
        {
            return;
        }

        await backupService.RestoreBackupAsync(
            record.OperationId,
            record.BackupFilePath ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少备份文件路径，无法回滚数据库。"),
            record.BackupSha256 ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少备份哈希，无法回滚数据库。"),
            cancellationToken);
    }

    private async Task RestoreCurrentImageTagAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.OldImageId))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少旧镜像 ID，无法回滚。");
        }

        await dockerAppManager.TagImageAsync(
            record.OldImageId,
            UpdateContractConstants.AppCurrentImageRepository,
            UpdateContractConstants.AppCurrentImageTagName,
            cancellationToken);
    }

    private async Task RestoreOldContainerAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.OldContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.OldContainerName);

        var oldContainer = await dockerAppManager.FindContainerByIdAsync(record.OldContainerId, cancellationToken);
        if (oldContainer is not null)
        {
            if (!string.Equals(oldContainer.ContainerName, record.OldContainerName, StringComparison.Ordinal))
            {
                await dockerAppManager.RenameContainerAsync(oldContainer.ContainerId, record.OldContainerName, cancellationToken);
            }

            await dockerAppManager.StartContainerAsync(oldContainer.ContainerId, cancellationToken);
            return;
        }

        var snapshot = record.OldContainerSnapshot
            ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "旧容器已删除且操作记录缺少容器快照，无法回滚。");
        var recreatedId = await dockerAppManager.CreateAppContainerAsync(
            snapshot,
            record.OldImageId ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少旧镜像 ID，无法重建旧容器。"),
            record.OperationId,
            cancellationToken);
        await dockerAppManager.StartContainerAsync(recreatedId, cancellationToken);
    }

    private async Task VerifyOldAppAsync(InstallOperationRecord record, CancellationToken cancellationToken)
    {
        var verification = await healthVerifier.VerifyAsync(
            record.CurrentVersion,
            expectedMaintenanceMode: false,
            options.VerifyConsecutiveSuccesses,
            TimeSpan.FromSeconds(options.VerifyTimeoutSeconds),
            cancellationToken);
        if (!verification.Success)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                verification.FailureReason ?? "旧版本健康验证失败。");
        }
    }

    private async Task<InstallOperationRecord> TransitionAsync(
        InstallOperationRecord record,
        string state,
        string? error,
        CancellationToken cancellationToken)
    {
        record = record with
        {
            State = state,
            UpdatedAt = timeProvider.GetUtcNow(),
            LastError = error,
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return record;
    }
}
