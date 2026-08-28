using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Scheduling;

internal sealed class DatabaseReportTaskExecutor(
    ReportDbContext dbContext,
    IReportService reportService,
    IReportDeliveryService deliveryService,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ILogger<DatabaseReportTaskExecutor> logger) : IReportTaskExecutor
{
    public async Task ExecuteAsync(
        Guid runId,
        bool recovering,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ReportRuns
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken)
            ?? throw new ReportTaskRunNotFoundException(runId);
        if (run.IsTerminal)
        {
            return;
        }

        if (recovering && run.Status == ReportRunStatus.Delivering)
        {
            await deliveryService.DeliverTaskAsync(
                new DeliverReportTaskCommand(run.Id, true),
                cancellationToken);
            await WriteAuditAsync(run, cancellationToken);
            return;
        }

        if (recovering && run.Status is ReportRunStatus.Collecting or ReportRunStatus.Rendering
            or ReportRunStatus.Running)
        {
            run.Fail("interrupted", "任务在应用重启前未完成。", timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await WriteAuditAsync(run, CancellationToken.None);
            return;
        }

        if (run.Status != ReportRunStatus.Queued)
        {
            run.Fail("invalid_state", null, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await WriteAuditAsync(run, CancellationToken.None);
            return;
        }

        try
        {
            var report = await ResolveReportAsync(run, cancellationToken);
            if (report.Status == ReportStatus.Partial)
            {
                if (run.Status == ReportRunStatus.Queued)
                {
                    run.BeginCollecting(timeProvider.GetUtcNow());
                }

                run.CompleteWithoutDelivery(
                    ReportRunStatus.PartialFailed,
                    "partial_report",
                    "报告存在采集失败范围，未自动发送。",
                    timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await deliveryService.DeliverTaskAsync(
                    new DeliverReportTaskCommand(run.Id, false),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailIfActiveAsync(run, "cancelled", "任务已取消。", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            ReportTaskLog.Failed(logger, exception, run.Id);
            await FailIfActiveAsync(
                run,
                DescribeErrorCode(exception),
                DescribeErrorMessage(exception),
                CancellationToken.None);
        }

        await WriteAuditAsync(run, CancellationToken.None);
    }

    private async Task<ReportDocument> ResolveReportAsync(
        ReportRun run,
        CancellationToken cancellationToken)
    {
        if (run.RetryOfRunId is not null)
        {
            var source = await dbContext.ReportRuns
                .AsNoTracking()
                .Include(item => item.Deliveries)
                .SingleOrDefaultAsync(item => item.Id == run.RetryOfRunId.Value, cancellationToken)
                ?? throw new ReportTaskRunNotFoundException(run.RetryOfRunId.Value);
            var hasOutcomeUnknown = source.Deliveries.Any(delivery =>
                delivery.ErrorCode == "outcome_unknown");
            if (hasOutcomeUnknown && !run.OutcomeUnknownConfirmed)
            {
                throw new ReportTaskOutcomeUnknownConfirmationRequiredException(source.Id);
            }

            if (source.ReportSnapshotId is not null)
            {
                var existing = await reportService.GetAsync(
                    source.ReportSnapshotId.Value,
                    cancellationToken)
                    ?? throw new ReportNotFoundException(source.ReportSnapshotId.Value);
                run.ReuseSnapshot(source.ReportSnapshotId.Value);
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }
        }

        run.BeginCollecting(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        var windows = ResolveFrozenWindows(run);
        return await reportService.GenerateTaskReportAsync(
            new GenerateTaskReportCommand(
                run.Id,
                run.PeriodEnd ?? throw new InvalidOperationException("The report period end is missing."),
                run.Timezone ?? throw new InvalidOperationException("The report timezone is missing."),
                windows,
                MapTrigger(run.Trigger)),
            cancellationToken);
    }

    /// <summary>Uses the frozen resolved windows; legacy queued runs fall back to the old two rolling windows.</summary>
    private static IReadOnlyList<ResolvedReportWindow> ResolveFrozenWindows(ReportRun run)
    {
        if (run.ResolvedWindowsJson is not null)
        {
            return ReportWindowJson.DeserializeResolved(run.ResolvedWindowsJson);
        }

        return ReportWindows.Resolve(
            ReportWindows.LegacyDefault,
            run.PeriodEnd ?? throw new InvalidOperationException("The report period end is missing."),
            allowCustomRange: true);
    }

    private async Task FailIfActiveAsync(
        ReportRun run,
        string errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (!run.IsTerminal)
        {
            run.Fail(errorCode, errorMessage, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Task WriteAuditAsync(ReportRun run, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            "system",
            "report.schedule.execute",
            run.Id.ToString("D"),
            run.Status.ToString(),
            null,
            $"{{\"trigger\":\"{run.Trigger}\",\"attempt\":{run.Attempt}}}",
            cancellationToken);

    private static ReportTrigger MapTrigger(ReportRunTrigger trigger) => trigger switch
    {
        ReportRunTrigger.Scheduled => ReportTrigger.Scheduled,
        ReportRunTrigger.ManualScheduled => ReportTrigger.ManualScheduled,
        ReportRunTrigger.Retry => ReportTrigger.Retry,
        _ => throw new InvalidOperationException("The report task trigger is invalid."),
    };

    private static string DescribeErrorCode(Exception exception) => exception switch
    {
        Sub2ApiConnectionNotConfiguredException => "connection_not_configured",
        Sub2ApiUserScopeException => "user_scope",
        Sub2ApiConnectionConflictException => "connection_changed",
        Sub2ApiClientException => "upstream_failed",
        ReportGenerationPreconditionException => "report_precondition",
        ReportDeliveryPreconditionException => "delivery_precondition",
        ReportTaskOutcomeUnknownConfirmationRequiredException => "outcome_unknown_confirmation_required",
        ReportNotFoundException => "report_not_found",
        _ => "internal_error",
    };

    private static string DescribeErrorMessage(Exception exception) => exception switch
    {
        Sub2ApiConnectionNotConfiguredException => "Sub2API 连接尚未配置。",
        Sub2ApiUserScopeException => "Sub2API 统计用户范围无效。",
        Sub2ApiConnectionConflictException => "Sub2API 配置在任务执行期间发生变化。",
        Sub2ApiClientException => "Sub2API 数据刷新或采集失败。",
        ReportGenerationPreconditionException => "报告生成条件不满足。",
        ReportDeliveryPreconditionException => "报告投递条件不满足。",
        ReportTaskOutcomeUnknownConfirmationRequiredException => "存在发送结果未知的渠道，需要显式确认后重试。",
        ReportNotFoundException => "任务关联的报告快照不存在。",
        _ => "任务因内部错误终止。",
    };
}

internal static partial class ReportTaskLog
{
    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Error,
        Message = "Report task {ReportRunId} failed")]
    public static partial void Failed(ILogger logger, Exception exception, Guid reportRunId);
}
