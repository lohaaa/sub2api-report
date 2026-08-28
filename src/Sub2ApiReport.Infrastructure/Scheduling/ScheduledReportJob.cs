using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Scheduling;

[DisallowConcurrentExecution]
internal sealed class ScheduledReportJob(
    ReportDbContext dbContext,
    IReportTaskExecutor executor,
    TimeProvider timeProvider,
    ILogger<ScheduledReportJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var configuredRunId = context.MergedJobDataMap.GetString(
            QuartzReportScheduleCoordinator.RunIdKey);
        ReportRun? run = null;
        if (Guid.TryParse(configuredRunId, out var runId))
        {
            run = await dbContext.ReportRuns
                .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        }
        else if (context.Recovering)
        {
            run = await dbContext.ReportRuns
                .Where(item => item.Trigger != ReportRunTrigger.ManualDelivery
                    && item.Status != ReportRunStatus.Succeeded
                    && item.Status != ReportRunStatus.PartialFailed
                    && item.Status != ReportRunStatus.Failed)
                .OrderBy(item => item.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        run ??= await CreateScheduledRunAsync(context, cancellationToken);
        if (run is null || run.IsTerminal)
        {
            return;
        }

        ScheduleLog.Executing(logger, run.Id, context.Trigger.Key);
        await executor.ExecuteAsync(run.Id, context.Recovering, cancellationToken);
    }

    private async Task<ReportRun?> CreateScheduledRunAsync(
        IJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var schedule = await dbContext.ReportSchedules
            .AsNoTracking()
            .SingleAsync(item => item.Id == ReportSchedule.SingletonId, cancellationToken);
        if (!schedule.Enabled)
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
        var fireTime = context.ScheduledFireTimeUtc ?? timeProvider.GetUtcNow();
        var localDate = TimeZoneInfo.ConvertTime(fireTime, zone).Date;
        var periodEnd = DateOnly.FromDateTime(localDate).AddDays(-1);
        var (specsJson, resolvedJson) = DatabaseReportScheduleService.FreezeWindows(
            schedule.WindowSpecsJson,
            periodEnd);
        var idempotencyKey = FormattableString.Invariant(
            $"scheduled:{schedule.Id}:{periodEnd:yyyy-MM-dd}");
        var existing = await dbContext.ReportRuns
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var run = ReportRun.QueueScheduled(
            schedule.Id,
            schedule.Revision,
            periodEnd,
            schedule.Timezone,
            specsJson,
            resolvedJson,
            idempotencyKey,
            timeProvider.GetUtcNow());
        dbContext.ReportRuns.Add(run);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var concurrent = await dbContext.ReportRuns
                .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return concurrent;
        }
    }
}
