namespace Sub2ApiReport.Domain.Reports;

public enum ReportRunTrigger
{
    ManualDelivery,
    Scheduled,
    ManualScheduled,
    Retry,
}

public enum ReportRunStatus
{
    Running,
    Queued,
    Collecting,
    Rendering,
    Delivering,
    Succeeded,
    PartialFailed,
    Failed,
}

public sealed class ReportRun
{
    public const int ErrorCodeMaxLength = 64;
    public const int ErrorMessageMaxLength = 512;

    private ReportRun()
    {
    }

    public Guid Id { get; private init; }

    public Guid? ReportSnapshotId { get; private set; }

    public ReportRunTrigger Trigger { get; private init; }

    public ReportRunStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private init; }

    public DateTimeOffset? CollectingAt { get; private set; }

    public DateTimeOffset? RenderingAt { get; private set; }

    public DateTimeOffset? DeliveringAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? IdempotencyKey { get; private init; }

    public int? ScheduleId { get; private init; }

    public long? ScheduleRevision { get; private init; }

    public DateOnly? PeriodEnd { get; private init; }

    public string? Timezone { get; private init; }

    /// <summary>Gets the frozen serialized window specifications; null on legacy queued runs.</summary>
    public string? WindowSpecsJson { get; private init; }

    /// <summary>Gets the frozen serialized resolved windows; null on legacy queued runs.</summary>
    public string? ResolvedWindowsJson { get; private init; }

    public Guid? RetryOfRunId { get; private init; }

    public int Attempt { get; private init; } = 1;

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool OutcomeUnknownConfirmed { get; private init; }

    public List<DeliveryRecord> Deliveries { get; private init; } = [];

    public static ReportRun StartManual(Guid reportSnapshotId, DateTimeOffset startedAt)
    {
        if (reportSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("The report snapshot identifier is required.", nameof(reportSnapshotId));
        }

        return new ReportRun
        {
            Id = Guid.NewGuid(),
            ReportSnapshotId = reportSnapshotId,
            Trigger = ReportRunTrigger.ManualDelivery,
            Status = ReportRunStatus.Running,
            StartedAt = startedAt,
            DeliveringAt = startedAt,
        };
    }

    public static ReportRun QueueScheduled(
        int scheduleId,
        long scheduleRevision,
        DateOnly periodEnd,
        string timezone,
        string? windowSpecsJson,
        string? resolvedWindowsJson,
        string idempotencyKey,
        DateTimeOffset queuedAt) => Queue(
            ReportRunTrigger.Scheduled,
            scheduleId,
            scheduleRevision,
            periodEnd,
            timezone,
            windowSpecsJson,
            resolvedWindowsJson,
            idempotencyKey,
            null,
            1,
            queuedAt);

    public static ReportRun QueueManualScheduled(
        int scheduleId,
        long scheduleRevision,
        DateOnly periodEnd,
        string timezone,
        string? windowSpecsJson,
        string? resolvedWindowsJson,
        DateTimeOffset queuedAt) => Queue(
            ReportRunTrigger.ManualScheduled,
            scheduleId,
            scheduleRevision,
            periodEnd,
            timezone,
            windowSpecsJson,
            resolvedWindowsJson,
            null,
            null,
            1,
            queuedAt);

    public static ReportRun QueueRetry(
        ReportRun previous,
        bool confirmOutcomeUnknown,
        DateTimeOffset queuedAt)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (!previous.IsTaskRetryable || previous.ScheduleId is null || previous.ScheduleRevision is null
            || previous.PeriodEnd is null || previous.Timezone is null)
        {
            throw new InvalidOperationException("The report task execution cannot be retried.");
        }

        return Queue(
            ReportRunTrigger.Retry,
            previous.ScheduleId.Value,
            previous.ScheduleRevision.Value,
            previous.PeriodEnd.Value,
            previous.Timezone,
            previous.WindowSpecsJson,
            previous.ResolvedWindowsJson,
            null,
            previous.Id,
            checked(previous.Attempt + 1),
            queuedAt,
            confirmOutcomeUnknown);
    }

    public void BeginCollecting(DateTimeOffset startedAt)
    {
        RequireStatus(ReportRunStatus.Queued);
        Status = ReportRunStatus.Collecting;
        CollectingAt = startedAt;
    }

    public void BeginRendering(DateTimeOffset startedAt)
    {
        RequireStatus(ReportRunStatus.Collecting);
        Status = ReportRunStatus.Rendering;
        RenderingAt = startedAt;
    }

    public void AttachSnapshot(Guid reportSnapshotId)
    {
        if (Status is not (ReportRunStatus.Collecting or ReportRunStatus.Rendering))
        {
            throw new InvalidOperationException("A snapshot can only be attached while generating a report.");
        }

        if (reportSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("The report snapshot identifier is required.", nameof(reportSnapshotId));
        }

        ReportSnapshotId = reportSnapshotId;
    }

    public void ReuseSnapshot(Guid reportSnapshotId)
    {
        RequireStatus(ReportRunStatus.Queued);
        if (reportSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("The report snapshot identifier is required.", nameof(reportSnapshotId));
        }

        ReportSnapshotId = reportSnapshotId;
    }

    public void BeginDelivering(DateTimeOffset startedAt)
    {
        if (Status is not (ReportRunStatus.Queued or ReportRunStatus.Rendering))
        {
            throw new InvalidOperationException("The report task is not ready for delivery.");
        }

        if (ReportSnapshotId is null)
        {
            throw new InvalidOperationException("A report snapshot is required before delivery.");
        }

        Status = ReportRunStatus.Delivering;
        DeliveringAt = startedAt;
    }

    public void Complete(ReportRunStatus status, DateTimeOffset completedAt)
    {
        if (Status is not (ReportRunStatus.Running or ReportRunStatus.Delivering))
        {
            throw new InvalidOperationException("A run can only complete from a delivery state.");
        }

        SetTerminalStatus(status, completedAt, null, null);
    }

    public void Fail(string errorCode, string? errorMessage, DateTimeOffset completedAt)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("A completed run cannot fail again.");
        }

        SetTerminalStatus(ReportRunStatus.Failed, completedAt, errorCode, errorMessage);
    }

    public void CompleteWithoutDelivery(
        ReportRunStatus status,
        string errorCode,
        string? errorMessage,
        DateTimeOffset completedAt)
    {
        if (Status is not (ReportRunStatus.Collecting or ReportRunStatus.Rendering))
        {
            throw new InvalidOperationException("Only report generation can complete without delivery.");
        }

        SetTerminalStatus(status, completedAt, errorCode, errorMessage);
    }

    public void RecordRetryResult(ReportRunStatus status, DateTimeOffset completedAt)
    {
        if (!IsRetryable)
        {
            throw new InvalidOperationException(
                "Only failed or partially failed runs can record retry results.");
        }

        SetTerminalStatus(status, completedAt, null, null);
    }

    public bool IsRetryable => Status is ReportRunStatus.PartialFailed or ReportRunStatus.Failed;

    public bool IsTaskRetryable => Trigger is not ReportRunTrigger.ManualDelivery && IsRetryable;

    public bool IsTerminal => Status is ReportRunStatus.Succeeded
        or ReportRunStatus.PartialFailed
        or ReportRunStatus.Failed;

    private static ReportRun Queue(
        ReportRunTrigger trigger,
        int scheduleId,
        long scheduleRevision,
        DateOnly periodEnd,
        string timezone,
        string? windowSpecsJson,
        string? resolvedWindowsJson,
        string? idempotencyKey,
        Guid? retryOfRunId,
        int attempt,
        DateTimeOffset queuedAt,
        bool outcomeUnknownConfirmed = false)
    {
        if (scheduleId != ReportSchedule.SingletonId || scheduleRevision <= 0)
        {
            throw new ArgumentException("The report schedule snapshot is invalid.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        if (timezone.Trim().Length > 100)
        {
            throw new ArgumentException("The time zone cannot exceed 100 characters.", nameof(timezone));
        }

        if (trigger == ReportRunTrigger.Scheduled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        }

        return new ReportRun
        {
            Id = Guid.NewGuid(),
            Trigger = trigger,
            Status = ReportRunStatus.Queued,
            StartedAt = queuedAt,
            ScheduleId = scheduleId,
            ScheduleRevision = scheduleRevision,
            PeriodEnd = periodEnd,
            Timezone = timezone.Trim(),
            WindowSpecsJson = windowSpecsJson,
            ResolvedWindowsJson = resolvedWindowsJson,
            IdempotencyKey = idempotencyKey,
            RetryOfRunId = retryOfRunId,
            Attempt = attempt,
            OutcomeUnknownConfirmed = outcomeUnknownConfirmed,
        };
    }

    private void SetTerminalStatus(
        ReportRunStatus status,
        DateTimeOffset completedAt,
        string? errorCode,
        string? errorMessage)
    {
        if (status is not (ReportRunStatus.Succeeded or ReportRunStatus.PartialFailed
            or ReportRunStatus.Failed))
        {
            throw new ArgumentException(
                "A run can only complete as succeeded, partially failed, or failed.",
                nameof(status));
        }

        Status = status;
        CompletedAt = completedAt;
        ErrorCode = errorCode is null ? null : ValidateErrorCode(errorCode);
        ErrorMessage = errorMessage is null
            ? null
            : errorMessage.Length <= ErrorMessageMaxLength
                ? errorMessage
                : errorMessage[..ErrorMessageMaxLength];
    }

    private void RequireStatus(ReportRunStatus requiredStatus)
    {
        if (Status != requiredStatus)
        {
            throw new InvalidOperationException($"The report task must be {requiredStatus}.");
        }
    }

    private static string ValidateErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        return normalized.Length <= ErrorCodeMaxLength
            ? normalized
            : throw new ArgumentException(
                $"The error code cannot exceed {ErrorCodeMaxLength} characters.",
                nameof(value));
    }
}
