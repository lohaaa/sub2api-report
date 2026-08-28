using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Scheduling;

internal sealed class DatabaseReportScheduleService(
    ReportDbContext dbContext,
    IReportScheduleCoordinator coordinator,
    TimeProvider timeProvider) : IReportScheduleService
{
    public async Task<ReportScheduleDocument> GetAsync(CancellationToken cancellationToken)
    {
        var schedule = await dbContext.ReportSchedules
            .AsNoTracking()
            .SingleAsync(item => item.Id == ReportSchedule.SingletonId, cancellationToken);
        var projection = await coordinator.GetProjectionAsync(
            MapSnapshot(schedule),
            cancellationToken);
        return Map(schedule, projection);
    }

    public async Task<ReportScheduleDocument> UpdateAsync(
        UpdateReportScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTimezone(command.Timezone);
        var specs = command.Windows ?? ReportWindows.Default;
        ReportWindows.Validate(specs, allowCustomRange: false);
        var specsJson = command.Windows is null ? null : ReportWindowJson.SerializeSpecs(specs);
        var schedule = await dbContext.ReportSchedules
            .SingleAsync(item => item.Id == ReportSchedule.SingletonId, cancellationToken);
        if (schedule.Revision != command.ExpectedRevision)
        {
            throw new ReportScheduleConflictException(command.ExpectedRevision, schedule.Revision);
        }

        schedule.Update(
            command.Enabled,
            command.DayOfMonth,
            command.LocalTime,
            command.Timezone,
            specsJson,
            timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReportScheduleConflictException(
                command.ExpectedRevision,
                command.ExpectedRevision + 1);
        }

        var snapshot = MapSnapshot(schedule);
        var projection = await coordinator.ApplyAsync(snapshot, cancellationToken);
        return Map(schedule, projection);
    }

    public async Task<ReportTaskRunDocument> RunNowAsync(CancellationToken cancellationToken)
    {
        var schedule = await dbContext.ReportSchedules
            .AsNoTracking()
            .SingleAsync(item => item.Id == ReportSchedule.SingletonId, cancellationToken);
        var periodEnd = ResolvePeriodEnd(schedule.Timezone);
        var (specsJson, resolvedJson) = FreezeWindows(schedule.WindowSpecsJson, periodEnd);
        var run = ReportRun.QueueManualScheduled(
            schedule.Id,
            schedule.Revision,
            periodEnd,
            schedule.Timezone,
            specsJson,
            resolvedJson,
            timeProvider.GetUtcNow());
        dbContext.ReportRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        await EnqueueOrFailAsync(run, cancellationToken);
        return MapRun(run);
    }

    public async Task<ReportTaskRunDocument> RetryAsync(
        RetryReportTaskCommand command,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.ReportRuns
            .Include(item => item.Deliveries)
            .SingleOrDefaultAsync(item => item.Id == command.RunId, cancellationToken)
            ?? throw new ReportTaskRunNotFoundException(command.RunId);
        if (!CanRetry(source))
        {
            throw new ReportTaskRunNotRetryableException(command.RunId);
        }

        var hasOutcomeUnknown = HasOutcomeUnknown(source);
        if (hasOutcomeUnknown && !command.ConfirmOutcomeUnknown)
        {
            throw new ReportTaskOutcomeUnknownConfirmationRequiredException(command.RunId);
        }

        var retry = ReportRun.QueueRetry(
            source,
            command.ConfirmOutcomeUnknown,
            timeProvider.GetUtcNow());
        dbContext.ReportRuns.Add(retry);
        await dbContext.SaveChangesAsync(cancellationToken);
        await EnqueueOrFailAsync(retry, cancellationToken);
        return MapRun(retry);
    }

    public async Task<ReportTaskRunPage> GetRunsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "The task run page is invalid.");
        }

        var query = dbContext.ReportRuns
            .AsNoTracking()
            .Include(item => item.Deliveries)
            .Where(item => item.Trigger != ReportRunTrigger.ManualDelivery);
        var total = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(item => item.StartedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new ReportTaskRunPage(
            runs.Select(MapRun).ToArray(),
            total,
            page,
            pageSize,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    private async Task EnqueueOrFailAsync(ReportRun run, CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.EnqueueAsync(run.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Fail("scheduler_unavailable", null, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw new ReportScheduleSynchronizationException("scheduler_unavailable");
        }
    }

    internal static (string? SpecsJson, string? ResolvedJson) FreezeWindows(
        string? windowSpecsJson,
        DateOnly periodEnd)
    {
        try
        {
            var specs = windowSpecsJson is null
                ? ReportWindows.Default
                : ReportWindowJson.DeserializeSpecs(windowSpecsJson);
            var resolved = ReportWindows.Resolve(specs, periodEnd, allowCustomRange: false);
            return (
                ReportWindowJson.SerializeSpecs(specs),
                ReportWindowJson.SerializeResolved(resolved));
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException)
        {
            throw new ReportGenerationPreconditionException(
                "The configured report windows are invalid.", exception);
        }
    }

    private DateOnly ResolvePeriodEnd(string timezone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var localDate = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).Date;
        return DateOnly.FromDateTime(localDate).AddDays(-1);
    }

    private static bool CanRetry(ReportRun run)
    {
        if (!run.IsTaskRetryable || run.ErrorCode == "partial_report")
        {
            return false;
        }

        return run.ReportSnapshotId is null
            || run.Deliveries.Count == 0
            || run.Deliveries.Any(delivery => delivery.Status == DeliveryStatus.Failed);
    }

    private static bool HasOutcomeUnknown(ReportRun run) => run.Deliveries.Any(delivery =>
        delivery.Status == DeliveryStatus.Failed
        && string.Equals(delivery.ErrorCode, "outcome_unknown", StringComparison.Ordinal));

    private static ReportScheduleSnapshot MapSnapshot(ReportSchedule schedule) => new(
        schedule.Id,
        schedule.Enabled,
        schedule.DayOfMonth,
        schedule.LocalTime,
        schedule.Timezone,
        schedule.WindowSpecsJson,
        schedule.Revision,
        schedule.UpdatedAt);

    private static ReportScheduleDocument Map(
        ReportSchedule schedule,
        ReportScheduleProjection projection)
    {
        var specs = schedule.WindowSpecsJson is null
            ? ReportWindows.Default
            : ReportWindowJson.DeserializeSpecs(schedule.WindowSpecsJson);
        return new ReportScheduleDocument(
            schedule.Enabled,
            schedule.DayOfMonth,
            schedule.LocalTime,
            schedule.Timezone,
            specs,
            schedule.Revision,
            schedule.UpdatedAt,
            projection.NextRunAt,
            projection.Synchronized,
            projection.ErrorCode);
    }

    internal static ReportTaskRunDocument MapRun(ReportRun run)
    {
        var succeeded = run.Deliveries.Count(item => item.Status == DeliveryStatus.Succeeded);
        var failed = run.Deliveries.Count(item => item.Status == DeliveryStatus.Failed);
        return new ReportTaskRunDocument(
            run.Id,
            run.Trigger,
            run.Status,
            run.ReportSnapshotId,
            run.PeriodEnd,
            run.Timezone,
            run.ScheduleRevision,
            run.RetryOfRunId,
            run.Attempt,
            run.StartedAt,
            run.CollectingAt,
            run.RenderingAt,
            run.DeliveringAt,
            run.CompletedAt,
            run.ErrorCode,
            run.ErrorMessage,
            run.Deliveries.Count,
            succeeded,
            failed,
            HasOutcomeUnknown(run),
            CanRetry(run));
    }

    private static void ValidateTimezone(string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("Unknown or invalid time zone.", nameof(timezone), exception);
        }
    }
}
