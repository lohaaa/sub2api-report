using Microsoft.Extensions.Logging;
using Quartz;
using Sub2ApiReport.Application.Scheduling;

namespace Sub2ApiReport.Infrastructure.Scheduling;

internal sealed class QuartzReportScheduleCoordinator(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzReportScheduleCoordinator> logger) : IReportScheduleCoordinator
{
    internal static readonly JobKey ReportJobKey = new("monthly-report", "reports");
    internal static readonly TriggerKey ScheduleTriggerKey = new("monthly-report-schedule", "reports");
    internal const string RunIdKey = "reportRunId";

    public async Task<ReportScheduleProjection> ApplyAsync(
        ReportScheduleSnapshot schedule,
        CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            await EnsureJobAsync(scheduler, cancellationToken);
            if (!schedule.Enabled)
            {
                _ = await scheduler.UnscheduleJob(ScheduleTriggerKey, cancellationToken);
                return new ReportScheduleProjection(null, true, null);
            }

            var trigger = BuildScheduleTrigger(schedule);
            DateTimeOffset? nextRunAt;
            if (await scheduler.CheckExists(ScheduleTriggerKey, cancellationToken))
            {
                nextRunAt = await scheduler.RescheduleJob(
                    ScheduleTriggerKey,
                    trigger,
                    cancellationToken);
            }
            else
            {
                nextRunAt = await scheduler.ScheduleJob(trigger, cancellationToken);
            }

            return new ReportScheduleProjection(nextRunAt, true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ScheduleLog.SynchronizationFailed(logger, exception, schedule.Revision);
            return new ReportScheduleProjection(null, false, "scheduler_unavailable");
        }
    }

    public async Task<ReportScheduleProjection> GetProjectionAsync(
        ReportScheduleSnapshot schedule,
        CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            var trigger = await scheduler.GetTrigger(ScheduleTriggerKey, cancellationToken);
            if (!schedule.Enabled)
            {
                return trigger is null
                    ? new ReportScheduleProjection(null, true, null)
                    : new ReportScheduleProjection(
                        trigger.GetNextFireTimeUtc(),
                        false,
                        "disabled_trigger_present");
            }

            return trigger is null
                ? new ReportScheduleProjection(null, false, "trigger_missing")
                : new ReportScheduleProjection(trigger.GetNextFireTimeUtc(), true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ScheduleLog.ProjectionReadFailed(logger, exception);
            return new ReportScheduleProjection(null, false, "scheduler_unavailable");
        }
    }

    public async Task EnqueueAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("The report run identifier is required.", nameof(runId));
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await EnsureJobAsync(scheduler, cancellationToken);
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"report-run-{runId:N}", "report-runs")
            .ForJob(ReportJobKey)
            .UsingJobData(RunIdKey, runId.ToString("D"))
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithMisfireHandlingInstructionFireNow())
            .Build();
        await scheduler.ScheduleJob(trigger, cancellationToken);
    }

    private static ITrigger BuildScheduleTrigger(ReportScheduleSnapshot schedule)
    {
        var localTime = Domain.Reports.ReportSchedule.ParseLocalTime(schedule.LocalTime);
        var cron = FormattableString.Invariant(
            $"0 {localTime.Minute} {localTime.Hour} {schedule.DayOfMonth} * ?");
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
        return TriggerBuilder.Create()
            .WithIdentity(ScheduleTriggerKey)
            .ForJob(ReportJobKey)
            .WithCronSchedule(cron, builder => builder
                .InTimeZone(timezone)
                .WithMisfireHandlingInstructionFireAndProceed())
            .Build();
    }

    private static async Task EnsureJobAsync(
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        if (await scheduler.CheckExists(ReportJobKey, cancellationToken))
        {
            return;
        }

        var job = JobBuilder.Create<ScheduledReportJob>()
            .WithIdentity(ReportJobKey)
            .StoreDurably()
            .RequestRecovery()
            .Build();
        await scheduler.AddJob(job, false, cancellationToken);
    }
}

internal static partial class ScheduleLog
{
    [LoggerMessage(
        EventId = 50,
        Level = LogLevel.Error,
        Message = "Could not synchronize report schedule revision {Revision}")]
    public static partial void SynchronizationFailed(ILogger logger, Exception exception, long revision);

    [LoggerMessage(
        EventId = 51,
        Level = LogLevel.Warning,
        Message = "Could not read the persistent report schedule trigger")]
    public static partial void ProjectionReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 52,
        Level = LogLevel.Information,
        Message = "Executing report task {ReportRunId} from trigger {TriggerKey}")]
    public static partial void Executing(ILogger logger, Guid reportRunId, TriggerKey triggerKey);
}
