using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Api.Updates;

internal sealed class InternalUpdaterTokenFilter(UpdaterSharedTokenProvider tokenProvider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
        if (token.Length == 0 || !tokenProvider.Matches(token))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "缺少或无效的 Updater 访问令牌。");
        }

        return await next(context);
    }
}

internal sealed class MaintenanceCoordinator(
    MaintenanceState maintenanceState,
    ReportDbContext dbContext,
    ISchedulerFactory schedulerFactory,
    ISystemInfoService systemInfoService)
{
    private static readonly ReportRunStatus[] ActiveStatuses =
    [
        ReportRunStatus.Running,
        ReportRunStatus.Queued,
        ReportRunStatus.Collecting,
        ReportRunStatus.Rendering,
        ReportRunStatus.Delivering,
    ];

    public async Task<AppUpdateHandshakeResponse> GetHandshakeAsync(CancellationToken cancellationToken)
    {
        var version = await systemInfoService.GetVersionAsync(cancellationToken);
        var migrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        var snapshot = maintenanceState.Current;
        return new AppUpdateHandshakeResponse(
            version.Version,
            UpdateContractConstants.DeploymentContractVersion,
            snapshot.Active,
            snapshot.State,
            snapshot.OperationId,
            migrations.LastOrDefault());
    }

    public async Task EnterAsync(string operationId, CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var current = maintenanceState.Current;
        if (current.Active)
        {
            if (string.Equals(current.OperationId, operationId, StringComparison.Ordinal))
            {
                return;
            }

            throw new MaintenanceConflictException("另一个升级维护操作正在进行中。");
        }

        if (await dbContext.ReportRuns.AnyAsync(run => ActiveStatuses.Contains(run.Status), cancellationToken))
        {
            throw new MaintenanceConflictException("存在正在执行或排队的报告任务，暂时不能升级。");
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Standby(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
            if (!maintenanceState.TryEnter(operationId, out _))
            {
                throw new MaintenanceConflictException("无法进入维护模式。");
            }
        }
        catch
        {
            await scheduler.Start(cancellationToken);
            throw;
        }
    }

    public async Task CompleteAsync(string operationId, CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var current = maintenanceState.Current;
        if (!current.Active || !string.Equals(current.OperationId, operationId, StringComparison.Ordinal))
        {
            throw new MaintenanceConflictException("维护操作标识不匹配。");
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Start(cancellationToken);
        if (!maintenanceState.TryComplete(operationId))
        {
            await scheduler.Standby(cancellationToken);
            throw new MaintenanceConflictException("维护状态已发生变化。");
        }
    }

    private static void ValidateOperationId(string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out _))
        {
            throw new ArgumentException("操作标识必须是 32 位 GUID。", nameof(operationId));
        }
    }
}

internal sealed class MaintenanceConflictException(string message) : Exception(message);

internal sealed class CandidateMaintenanceStartupService(
    MaintenanceState maintenanceState,
    ISchedulerFactory schedulerFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (maintenanceState.Current.CandidateVerification)
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            await scheduler.Standby(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
