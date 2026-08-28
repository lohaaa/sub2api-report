using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Services;

public sealed record InstallSubmissionResult(
    bool Accepted,
    InstallOperationRecord? Operation,
    int StatusCode,
    string? Detail)
{
    public static InstallSubmissionResult Reject(int statusCode, string detail) =>
        new(false, null, statusCode, detail);
}

public interface IInstallService
{
    Task<InstallSubmissionResult> SubmitAsync(InstallUpdateRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 安装请求入口：执行全部安装门禁（配置开关、缓存验签结果、manualUpgradeRequired、
/// onlineInstallSupported、版本一致性、队列忙碌），通过后创建持久化操作并入队。
/// </summary>
public sealed class InstallService(
    UpdateOptions options,
    UpdateStateStore stateStore,
    IReleaseCacheService releaseCache,
    IInstallCoordinator queue,
    TimeProvider timeProvider) : IInstallService
{
    public async Task<InstallSubmissionResult> SubmitAsync(
        InstallUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentVersion);

        if (!options.InstallationEnabled)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "在线安装未启用。");
        }

        if (!SemanticVersion.TryParse(request.CurrentVersion, out var currentVersion) || currentVersion is null)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status400BadRequest,
                "CurrentVersion 不是有效的 SemVer。");
        }

        var cached = await releaseCache.LoadVerifiedAsync(cancellationToken);
        if (cached is null)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "暂无有效的更新检查结果，请先执行检查。");
        }

        var manifest = cached.Manifest;
        if (manifest.ManualUpgradeRequired)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "该版本要求手工完整 bundle 升级，不提供在线安装。");
        }

        if (!manifest.OnlineInstallSupported)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "该版本不支持在线安装。");
        }

        var targetVersionText = request.TargetVersion ?? manifest.Version;
        if (!string.Equals(targetVersionText, manifest.Version, StringComparison.Ordinal))
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "目标版本与最新检查结果不一致。");
        }

        if (!SemanticVersion.TryParse(targetVersionText, out var targetVersion) || targetVersion is null)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status400BadRequest,
                "TargetVersion 不是有效的 SemVer。");
        }

        if (currentVersion.CompareTo(targetVersion) >= 0)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "当前版本必须严格低于目标版本。");
        }

        var existingOperations = await stateStore.LoadAllOperationsAsync(cancellationToken);
        if (existingOperations.Any(operation => !InstallOperationStates.IsTerminal(operation.State)))
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "另一个升级操作正在进行中。");
        }

        if (queue.IsBusy)
        {
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "另一个升级操作正在进行中。");
        }

        var now = timeProvider.GetUtcNow();
        var operation = new InstallOperationRecord(
            OperationId: Guid.NewGuid().ToString("N"),
            State: InstallOperationStates.Queued,
            CurrentVersion: request.CurrentVersion,
            TargetVersion: targetVersionText,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: null,
            LastError: null,
            MaintenanceEntered: false,
            BackupCompleted: false,
            PreflightReport: null,
            ArchiveFilePath: null,
            ArchiveSha256: null,
            LoadedImageId: null,
            BackupFilePath: null,
            BackupSha256: null,
            OldContainerId: null,
            OldContainerName: null,
            OldImageId: null,
            CandidateContainerId: null,
            OldContainerSnapshot: null,
            Stages: []);

        await stateStore.SaveOperationAsync(operation, cancellationToken);
        if (!queue.TryEnqueue(operation.OperationId))
        {
            await stateStore.DeleteOperationAsync(operation.OperationId, cancellationToken);
            return InstallSubmissionResult.Reject(
                StatusCodes.Status409Conflict,
                "另一个升级操作正在进行中。");
        }

        return new InstallSubmissionResult(true, operation, StatusCodes.Status202Accepted, null);
    }
}
