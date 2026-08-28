using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Updater.Maintenance;

public sealed record HealthVerificationResult(bool Success, int ObservedConsecutiveSuccesses, string? FailureReason)
{
    public static HealthVerificationResult Failure(string reason) => new(false, 0, reason);
}

/// <summary>
/// 升级健康验证：要求在超时窗口内连续 N 次同时满足
/// /health/live、/health/ready、内部握手（版本 / 部署契约 / 维护模式）全部符合预期。
/// </summary>
public interface IHealthVerifier
{
    Task<HealthVerificationResult> VerifyAsync(
        string expectedVersion,
        bool expectedMaintenanceMode,
        string? expectedOperationId,
        int requiredConsecutiveSuccesses,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>生产实现：轮询 App 内部健康端点与维护握手，任何一次失败都会重置连续成功计数。</summary>
public sealed class AppHealthVerifier(
    HttpClient httpClient,
    IAppMaintenanceClient maintenanceClient,
    TimeProvider timeProvider) : IHealthVerifier
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public async Task<HealthVerificationResult> VerifyAsync(
        string expectedVersion,
        bool expectedMaintenanceMode,
        string? expectedOperationId,
        int requiredConsecutiveSuccesses,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredConsecutiveSuccesses);

        var deadline = timeProvider.GetUtcNow() + timeout;
        var consecutive = 0;
        string? lastFailure = null;

        while (true)
        {
            var (attemptSucceeded, failure) = await TryVerifyOnceAsync(
                expectedVersion,
                expectedMaintenanceMode,
                expectedOperationId,
                cancellationToken);
            consecutive = attemptSucceeded ? consecutive + 1 : 0;
            if (consecutive >= requiredConsecutiveSuccesses)
            {
                return new HealthVerificationResult(true, consecutive, null);
            }

            lastFailure ??= failure;
            if (timeProvider.GetUtcNow() + PollInterval > deadline)
            {
                return new HealthVerificationResult(
                    false,
                    consecutive,
                    lastFailure ?? "健康验证超时。");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<(bool Succeeded, string? FailureReason)> TryVerifyOnceAsync(
        string expectedVersion,
        bool expectedMaintenanceMode,
        string? expectedOperationId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var liveResponse = await httpClient.GetAsync("/health/live", cancellationToken);
            if (!liveResponse.IsSuccessStatusCode)
            {
                return (false, "/health/live 请求失败。");
            }

            if (!expectedMaintenanceMode)
            {
                using var readyResponse = await httpClient.GetAsync("/health/ready", cancellationToken);
                if (!readyResponse.IsSuccessStatusCode)
                {
                    return (false, "/health/ready 请求失败。");
                }
            }

            var handshake = await maintenanceClient.GetHandshakeAsync(cancellationToken);
            if (!string.Equals(handshake.Version, expectedVersion, StringComparison.Ordinal))
            {
                return (false, "App 版本与预期不一致。");
            }

            if (handshake.DeploymentContractVersion != UpdateContractConstants.DeploymentContractVersion)
            {
                return (false, "App 部署契约版本与预期不一致。");
            }

            if (handshake.MaintenanceMode != expectedMaintenanceMode)
            {
                return (false, "App 维护模式状态与预期不一致。");
            }

            if (!string.Equals(
                    handshake.MaintenanceOperationId,
                    expectedOperationId,
                    StringComparison.Ordinal))
            {
                return (false, "App 维护操作标识与预期不一致。");
            }

            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }
}
