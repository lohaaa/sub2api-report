using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Scheduling;

internal sealed class QuartzReportScheduleCoordinator(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzReportScheduleCoordinator> logger) : IReportScheduleCoordinator
{
    internal static readonly JobKey ReportJobKey = new("monthly-report", "reports");
    internal static readonly TriggerKey ScheduleTriggerKey = new("monthly-report-schedule", "reports");
    internal static readonly TriggerKey ShortMonthFallbackTriggerKey = new(
        "monthly-report-short-month",
        "reports");
    internal const string TriggerGroup = "reports";
    internal const string RunIdKey = "reportRunId";

    public async Task<ReportScheduleProjection> ApplyAsync(
        ReportScheduleSnapshot schedule,
        CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            await EnsureJobAsync(scheduler, cancellationToken);
            var desiredTriggers = BuildDesiredTriggers(schedule);
            var managedKeys = (await scheduler.GetTriggerKeys(
                GroupMatcher<TriggerKey>.GroupEquals(TriggerGroup),
                cancellationToken)).ToList();
            foreach (var staleKey in managedKeys.Where(key =>
                desiredTriggers.All(trigger => !trigger.Key.Equals(key))))
            {
                _ = await scheduler.UnscheduleJob(staleKey, cancellationToken);
            }

            DateTimeOffset? nextRunAt = null;
            foreach (var trigger in desiredTriggers)
            {
                var triggerNextRunAt = await scheduler.CheckExists(trigger.Key, cancellationToken)
                    ? await scheduler.RescheduleJob(trigger.Key, trigger, cancellationToken)
                    : await scheduler.ScheduleJob(trigger, cancellationToken);
                nextRunAt = Min(nextRunAt, triggerNextRunAt);
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
            var managedTriggers = await scheduler.GetTriggersOfJob(ReportJobKey, cancellationToken);
            if (!schedule.Enabled)
            {
                return managedTriggers.Count == 0
                    ? new ReportScheduleProjection(null, true, null)
                    : new ReportScheduleProjection(
                        MinNextFireTime(managedTriggers),
                        false,
                        "disabled_trigger_present");
            }

            var desired = BuildDesiredTriggers(schedule).ToList();
            var desiredKeys = desired.Select(trigger => trigger.Key).ToList();
            var activeTriggers = managedTriggers
                .Where(trigger => desiredKeys.Any(desiredKey => desiredKey.Equals(trigger.Key)))
                .ToList();
            var unexpectedTriggers = managedTriggers
                .Where(trigger => desiredKeys.All(desiredKey => !desiredKey.Equals(trigger.Key)))
                .ToList();
            if (activeTriggers.Count != desired.Count || unexpectedTriggers.Count > 0)
            {
                return new ReportScheduleProjection(
                    MinNextFireTime(activeTriggers),
                    false,
                    "trigger_set_mismatch");
            }

            foreach (var (expected, actual) in desired
                .Select(expectedTrigger => (expectedTrigger, actual: activeTriggers.Single(trigger =>
                    expectedTrigger.Key.Equals(trigger.Key)))))
            {
                if (actual is not ICronTrigger cronTrigger
                    || !string.Equals(
                        cronTrigger.CronExpressionString,
                        ((ICronTrigger)expected).CronExpressionString,
                        StringComparison.Ordinal)
                    || !Equals(cronTrigger.TimeZone, ((ICronTrigger)expected).TimeZone)
                    || actual.MisfireInstruction != expected.MisfireInstruction)
                {
                    return new ReportScheduleProjection(
                        MinNextFireTime(activeTriggers),
                        false,
                        "trigger_mismatch");
                }
            }

            return new ReportScheduleProjection(MinNextFireTime(activeTriggers), true, null);
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

    internal static IReadOnlyList<ITrigger> BuildDesiredTriggers(
        ReportScheduleSnapshot schedule)
    {
        if (!schedule.Enabled)
        {
            return [];
        }

        var localTime = ReportSchedule.ParseLocalTime(schedule.LocalTime);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
        var primaryCron = FormattableString.Invariant(
            $"0 {localTime.Minute} {localTime.Hour} {schedule.DayOfMonth} * ?");
        var primary = BuildCronTrigger(ScheduleTriggerKey, primaryCron, timezone);
        if (schedule.DayOfMonth <= 28 || schedule.ShortMonthStrategy == ShortMonthStrategy.SkipMonth)
        {
            return [primary];
        }

        var fallbackCron = FormattableString.Invariant(
            $"0 {localTime.Minute} {localTime.Hour} L * ?");
        return [primary, BuildCronTrigger(ShortMonthFallbackTriggerKey, fallbackCron, timezone)];
    }

    private static ITrigger BuildCronTrigger(
        TriggerKey key,
        string cron,
        TimeZoneInfo timezone) => TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(ReportJobKey)
            .WithCronSchedule(cron, builder => builder
                .InTimeZone(timezone)
                .WithMisfireHandlingInstructionFireAndProceed())
            .Build();

    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right) => (left, right) switch
    {
        (null, null) => null,
        (null, _) => right,
        (_, null) => left,
        (_, _) => left < right ? left : right,
    };

    private static DateTimeOffset? MinNextFireTime(
        IReadOnlyCollection<ITrigger> triggers)
    {
        DateTimeOffset? nextRunAt = null;
        foreach (var trigger in triggers)
        {
            nextRunAt = Min(nextRunAt, trigger.GetNextFireTimeUtc());
        }

        return nextRunAt;
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
