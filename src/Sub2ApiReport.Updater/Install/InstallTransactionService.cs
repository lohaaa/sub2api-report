using System.Security.Cryptography;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Install;

/// <summary>安装事务执行器：按阶段推进并持久化操作快照；任何失败走回滚或安全终态。</summary>
public interface IInstallTransaction
{
    Task<InstallOperationRecord> ExecuteAsync(InstallOperationRecord operation, CancellationToken cancellationToken);
}

public sealed class InstallTransactionService(
    IReleaseCacheService releaseCache,
    IDownloader downloader,
    IDockerAppManager dockerAppManager,
    IAppMaintenanceClient maintenanceClient,
    ISqliteBackupService backupService,
    IHealthVerifier healthVerifier,
    InstallRollbackService rollbackService,
    UpdateStateStore stateStore,
    UpdateOptions options,
    TimeProvider timeProvider) : IInstallTransaction
{
    public async Task<InstallOperationRecord> ExecuteAsync(
        InstallOperationRecord operation,
        CancellationToken cancellationToken)
    {
        var record = operation;
        var stages = record.Stages.ToList();

        // 失败处理使用 CancellationToken.None：即使在停机/取消路径上也要持久化安全终态。
        try
        {
            record = await RunPreflightAsync(record, stages, cancellationToken);
            record = await RunDownloadArchiveAsync(record, stages, cancellationToken);
            record = await RunVerifyArchiveAsync(record, stages, cancellationToken);
            record = await RunLoadImageAsync(record, stages, cancellationToken);
            record = await RunRequestMaintenanceAsync(record, stages, cancellationToken);
            record = await RunBackupAsync(record, stages, cancellationToken);
            record = await RunReplaceAppAsync(record, stages, cancellationToken);
            record = await RunVerifyAsync(record, stages, cancellationToken);
            record = await RunCompleteMaintenanceAsync(record, stages, cancellationToken);
            return await RunFinalizeAsync(record, stages, cancellationToken);
        }
        catch (Exception exception)
        {
            return await HandleFailureAsync(record, exception, cancellationToken);
        }
    }

    private async Task<InstallOperationRecord> RunPreflightAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(record, stages, InstallOperationStates.Preflight, cancellationToken);
        var errors = new List<string>();

        var cached = await releaseCache.LoadVerifiedAsync(cancellationToken);
        if (cached is null)
        {
            errors.Add("没有有效的已验签 Release 缓存，请先执行检查。");
        }
        else if (cached.Manifest.ManualUpgradeRequired)
        {
            errors.Add("该版本要求手工完整 bundle 升级。");
        }
        else if (!cached.Manifest.OnlineInstallSupported)
        {
            errors.Add("该版本不支持在线安装。");
        }
        else if (!string.Equals(cached.Manifest.Version, record.TargetVersion, StringComparison.Ordinal))
        {
            errors.Add("Release 缓存版本与操作目标版本不一致。");
        }

        var dockerReachable = false;
        AppContainerSnapshot? currentApp = null;
        try
        {
            currentApp = await dockerAppManager.FindAppContainerAsync(cancellationToken);
            dockerReachable = true;
            if (currentApp is null)
            {
                errors.Add("未找到当前 App 容器。");
            }
            else
            {
                errors.AddRange(AppContractMapper.ValidateContract(currentApp).Select(error => $"当前 App 容器契约无效：{error}"));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add($"Docker App 容器检查失败：{exception.Message}");
        }

        var currentAppReady = false;
        try
        {
            var handshake = await maintenanceClient.GetHandshakeAsync(cancellationToken);
            if (!string.Equals(handshake.Version, record.CurrentVersion, StringComparison.Ordinal))
            {
                errors.Add("当前 App 版本与操作记录不一致。");
            }
            else if (handshake.MaintenanceMode)
            {
                errors.Add("当前 App 已处于维护模式。");
            }
            else if (handshake.DeploymentContractVersion != UpdateContractConstants.DeploymentContractVersion)
            {
                errors.Add("当前 App 部署契约版本不受支持。");
            }
            else
            {
                currentAppReady = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add($"当前 App 维护握手失败：{exception.Message}");
        }

        var databaseAccessible = File.Exists(options.DatabasePath);
        if (!databaseAccessible)
        {
            errors.Add("SQLite 数据库文件不可访问。");
        }

        var stateDirectoryWritable = IsDirectoryWritable();
        if (!stateDirectoryWritable)
        {
            errors.Add("升级状态目录不可写。");
        }

        var report = new InstallPreflightReport(
            cached is not null,
            dockerReachable,
            currentAppReady,
            databaseAccessible,
            stateDirectoryWritable,
            errors);
        record = record with { PreflightReport = report };
        await stateStore.SaveOperationAsync(record, cancellationToken);

        if (errors.Count > 0)
        {
            throw new UpdateOperationException(
                StatusCodes.Status409Conflict,
                $"安装前检查未通过：{string.Join("；", errors)}");
        }

        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunDownloadArchiveAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(
            record,
            stages,
            InstallOperationStates.DownloadingArchive,
            cancellationToken);
        var manifest = (await GetCachedManifestAsync(record, cancellationToken)).Manifest;
        var downloadsDirectory = stateStore.GetDownloadsDirectory();
        var archivePath = Path.Combine(downloadsDirectory, $"{record.OperationId}.tar.gz");
        var download = await downloader.DownloadAsync(
            manifest.App.ArchiveUrl,
            archivePath,
            manifest.App.Size,
            cancellationToken);
        record = record with { ArchiveFilePath = download.FilePath };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunVerifyArchiveAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(
            record,
            stages,
            InstallOperationStates.VerifyingArchive,
            cancellationToken);
        var manifest = (await GetCachedManifestAsync(record, cancellationToken)).Manifest;
        var archivePath = record.ArchiveFilePath
            ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少归档文件路径。");
        await using (var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                hash.AppendData(buffer, 0, bytesRead);
            }

            var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (totalBytes != manifest.App.Size)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "App 归档大小与 manifest 不一致。");
            }

            if (!string.Equals(actualSha256, manifest.App.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "App 归档 SHA-256 校验失败。");
            }
        }

        record = record with { ArchiveSha256 = manifest.App.ArchiveSha256 };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunLoadImageAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(record, stages, InstallOperationStates.LoadingImage, cancellationToken);
        var manifest = (await GetCachedManifestAsync(record, cancellationToken)).Manifest;
        var archivePath = record.ArchiveFilePath
            ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "操作记录缺少归档文件路径。");
        await using var archiveStream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var imageId = await dockerAppManager.LoadImageArchiveAsync(
            archiveStream,
            manifest.App.ImageId,
            manifest.App.LoadedTag,
            manifest.Version,
            cancellationToken);
        await dockerAppManager.TagImageAsync(
            imageId,
            UpdateContractConstants.AppCurrentImageRepository,
            UpdateContractConstants.AppCurrentImageTagName,
            cancellationToken);
        record = record with { LoadedImageId = imageId };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunRequestMaintenanceAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(
            record,
            stages,
            InstallOperationStates.RequestingMaintenance,
            cancellationToken);
        await maintenanceClient.EnterMaintenanceAsync(record.OperationId, cancellationToken);
        var handshake = await maintenanceClient.GetHandshakeAsync(cancellationToken);
        if (!handshake.MaintenanceMode)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "App 未确认进入维护模式。");
        }

        record = record with { MaintenanceEntered = true };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunBackupAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(record, stages, InstallOperationStates.BackingUp, cancellationToken);
        var backup = await backupService.CreateBackupAsync(record.OperationId, cancellationToken);
        record = record with
        {
            BackupCompleted = true,
            BackupFilePath = backup.FilePath,
            BackupSha256 = backup.Sha256,
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunReplaceAppAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(record, stages, InstallOperationStates.ReplacingApp, cancellationToken);

        // 先持久化旧容器契约，再开始任何破坏性变更，保证中途崩溃也能回滚。
        var oldContainer = await dockerAppManager.FindAppContainerAsync(cancellationToken)
            ?? throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "未找到当前 App 容器，无法替换。");
        var contractErrors = AppContractMapper.ValidateContract(oldContainer);
        if (contractErrors.Count > 0)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                $"当前 App 容器契约无效：{string.Join("；", contractErrors)}");
        }

        record = record with
        {
            OldContainerId = oldContainer.ContainerId,
            OldContainerName = oldContainer.ContainerName,
            OldImageId = oldContainer.ImageId,
            OldContainerSnapshot = oldContainer,
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);

        await dockerAppManager.StopContainerAsync(
            oldContainer.ContainerId,
            options.ContainerStopWaitSeconds,
            cancellationToken);
        var renamedOldName = BuildRenamedOldContainerName(oldContainer.ContainerName, record.OperationId);
        await dockerAppManager.RenameContainerAsync(oldContainer.ContainerId, renamedOldName, cancellationToken);

        var candidateId = await dockerAppManager.CreateAppContainerAsync(
            oldContainer,
            record.LoadedImageId
                ?? throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "操作记录缺少目标镜像 ID，无法替换 App。"),
            record.OperationId,
            cancellationToken);
        record = record with { CandidateContainerId = candidateId };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        await dockerAppManager.StartContainerAsync(candidateId, cancellationToken);
        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunVerifyAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(record, stages, InstallOperationStates.Verifying, cancellationToken);
        var verification = await healthVerifier.VerifyAsync(
            record.TargetVersion,
            expectedMaintenanceMode: true,
            record.OperationId,
            options.VerifyConsecutiveSuccesses,
            TimeSpan.FromSeconds(options.VerifyTimeoutSeconds),
            cancellationToken);
        if (!verification.Success)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                verification.FailureReason ?? "候选 App 健康验证失败。");
        }

        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunCompleteMaintenanceAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        (record, stages) = await BeginStageAsync(
            record,
            stages,
            InstallOperationStates.CompletingMaintenance,
            cancellationToken);
        await maintenanceClient.CompleteMaintenanceAsync(record.OperationId, cancellationToken);
        var handshake = await maintenanceClient.GetHandshakeAsync(cancellationToken);
        if (handshake.MaintenanceMode)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "App 未确认解除维护模式。");
        }

        if (!string.Equals(handshake.Version, record.TargetVersion, StringComparison.Ordinal))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "解除维护模式后 App 版本与目标版本不一致。");
        }

        return await CompleteStageAsync(record, stages, cancellationToken);
    }

    private async Task<InstallOperationRecord> RunFinalizeAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        // 旧容器此时已改名且停止；清理为尽力而为，删除失败不阻断成功终态。
        if (!string.IsNullOrWhiteSpace(record.OldContainerId))
        {
            try
            {
                await dockerAppManager.RemoveContainerAsync(record.OldContainerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 保留旧容器由操作员清理，升级结果仍为成功。
            }
        }

        record = record with
        {
            State = InstallOperationStates.Succeeded,
            CompletedAt = timeProvider.GetUtcNow(),
            UpdatedAt = timeProvider.GetUtcNow(),
            LastError = null,
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return record;
    }

    private async Task<InstallOperationRecord> HandleFailureAsync(
        InstallOperationRecord record,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 失败阶段可能已在抛出异常前持久化了关键状态（旧容器/镜像/候选 ID），以磁盘上的最新记录为准。
        record = await stateStore.LoadOperationAsync(record.OperationId, CancellationToken.None) ?? record;
        var message = exception is UpdateOperationException updateException
            ? updateException.Message
            : $"安装失败：{exception.Message}";
        if (record.BackupCompleted)
        {
            return await rollbackService.RollbackAsync(record, message, CancellationToken.None);
        }

        // 备份尚未完成：数据库未被改动，只需尽力退出维护模式。
        if (record.MaintenanceEntered)
        {
            try
            {
                await maintenanceClient.CompleteMaintenanceAsync(record.OperationId, CancellationToken.None);
                record = record with { MaintenanceEntered = false };
            }
            catch (Exception exitException)
            {
                message = $"{message}；退出维护模式失败：{exitException.Message}";
            }
        }

        record = record with
        {
            State = InstallOperationStates.Failed,
            CompletedAt = timeProvider.GetUtcNow(),
            UpdatedAt = timeProvider.GetUtcNow(),
            LastError = message,
        };
        await stateStore.SaveOperationAsync(record, CancellationToken.None);
        return record;
    }

    private async Task<(InstallOperationRecord Record, List<InstallStageRecord> Stages)> BeginStageAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        string stage,
        CancellationToken cancellationToken)
    {
        var stageRecord = new InstallStageRecord(stage, timeProvider.GetUtcNow());
        stages.Add(stageRecord);
        record = record with
        {
            State = stage,
            UpdatedAt = timeProvider.GetUtcNow(),
            Stages = [.. stages],
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return (record, stages);
    }

    private async Task<InstallOperationRecord> CompleteStageAsync(
        InstallOperationRecord record,
        List<InstallStageRecord> stages,
        CancellationToken cancellationToken)
    {
        if (stages.Count > 0)
        {
            stages[^1] = stages[^1] with { CompletedAt = timeProvider.GetUtcNow() };
        }

        record = record with
        {
            UpdatedAt = timeProvider.GetUtcNow(),
            Stages = [.. stages],
        };
        await stateStore.SaveOperationAsync(record, cancellationToken);
        return record;
    }

    private async Task<CachedRelease> GetCachedManifestAsync(
        InstallOperationRecord record,
        CancellationToken cancellationToken)
    {
        var cached = await releaseCache.LoadVerifiedAsync(cancellationToken);
        if (cached is null || !string.Equals(cached.Manifest.Version, record.TargetVersion, StringComparison.Ordinal))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "Release 缓存缺失或与操作目标版本不一致。");
        }

        return cached;
    }

    private static string BuildRenamedOldContainerName(string containerName, string operationId)
    {
        var suffix = $"-old-{operationId}";
        var maxLength = 100;
        return containerName.Length + suffix.Length > maxLength
            ? containerName[..(maxLength - suffix.Length)] + suffix
            : containerName + suffix;
    }

    private bool IsDirectoryWritable()
    {
        try
        {
            var probePath = Path.Combine(stateStore.GetDownloadsDirectory(), $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
