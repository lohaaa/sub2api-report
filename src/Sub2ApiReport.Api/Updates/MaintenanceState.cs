using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sub2ApiReport.Api.Updates;

internal sealed record MaintenanceSnapshot(
    bool Active,
    bool CandidateVerification,
    string State,
    string? OperationId);

internal sealed class MaintenanceState
{
    private readonly object _lock = new();
    private MaintenanceSnapshot _snapshot;

    public MaintenanceState(IConfiguration configuration)
    {
        var candidateOperationId = configuration["Update:MaintenanceOperationId"]?.Trim();
        _snapshot = string.IsNullOrWhiteSpace(candidateOperationId)
            ? new(false, false, "normal", null)
            : new(true, true, "candidate_verification", candidateOperationId);
    }

    public MaintenanceSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _snapshot;
            }
        }
    }

    public bool TryEnter(string operationId, out string? conflictOperationId)
    {
        lock (_lock)
        {
            if (_snapshot.Active)
            {
                conflictOperationId = _snapshot.OperationId;
                return string.Equals(_snapshot.OperationId, operationId, StringComparison.Ordinal);
            }

            _snapshot = new(true, false, "runtime_maintenance", operationId);
            conflictOperationId = null;
            return true;
        }
    }

    public bool TryComplete(string operationId)
    {
        lock (_lock)
        {
            if (!_snapshot.Active || !string.Equals(_snapshot.OperationId, operationId, StringComparison.Ordinal))
            {
                return false;
            }

            _snapshot = new(false, false, "normal", null);
            return true;
        }
    }
}

internal sealed class MaintenanceReadinessHealthCheck(MaintenanceState maintenanceState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = maintenanceState.Current;
        var result = snapshot.Active && !snapshot.CandidateVerification
            ? HealthCheckResult.Unhealthy("Application maintenance is active.")
            : HealthCheckResult.Healthy();
        return Task.FromResult(result);
    }
}

internal sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MaintenanceState maintenanceState)
    {
        var snapshot = maintenanceState.Current;
        if (!snapshot.Active || IsAllowed(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: "系统正在执行升级维护，请稍后重试。",
                extensions: new Dictionary<string, object?>
                {
                    ["maintenanceState"] = snapshot.State,
                    ["operationId"] = snapshot.OperationId,
                    ["correlationId"] = context.TraceIdentifier,
                })
            .ExecuteAsync(context);
    }

    private static bool IsAllowed(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.Ordinal)
        || path.StartsWithSegments("/internal/v1", StringComparison.Ordinal)
        || path.StartsWithSegments("/api/v1/updates", StringComparison.Ordinal)
        || path.StartsWithSegments("/api/v1/security/antiforgery", StringComparison.Ordinal);
}
