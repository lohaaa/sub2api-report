using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Application.Scheduling;

public interface IReportScheduleService
{
    Task<ReportScheduleDocument> GetAsync(CancellationToken cancellationToken);

    Task<ReportScheduleDocument> UpdateAsync(
        UpdateReportScheduleCommand command,
        CancellationToken cancellationToken);

    Task<ReportTaskRunDocument> RunNowAsync(CancellationToken cancellationToken);

    Task<ReportTaskRunDocument> RetryAsync(
        RetryReportTaskCommand command,
        CancellationToken cancellationToken);

    Task<ReportTaskRunPage> GetRunsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public interface IReportTaskExecutor
{
    Task ExecuteAsync(Guid runId, bool recovering, CancellationToken cancellationToken);
}

public interface IReportScheduleCoordinator
{
    Task<ReportScheduleProjection> ApplyAsync(
        ReportScheduleSnapshot schedule,
        CancellationToken cancellationToken);

    Task<ReportScheduleProjection> GetProjectionAsync(
        ReportScheduleSnapshot schedule,
        CancellationToken cancellationToken);

    Task EnqueueAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed record ReportScheduleSnapshot(
    int Id,
    bool Enabled,
    int DayOfMonth,
    ShortMonthStrategy ShortMonthStrategy,
    string LocalTime,
    string Timezone,
    string? WindowSpecsJson,
    long Revision,
    DateTimeOffset? UpdatedAt);

public sealed record ReportScheduleProjection(
    DateTimeOffset? NextRunAt,
    bool Synchronized,
    string? ErrorCode);

public sealed record ReportScheduleDocument(
    bool Enabled,
    int DayOfMonth,
    ShortMonthStrategy ShortMonthStrategy,
    string LocalTime,
    string Timezone,
    IReadOnlyList<ReportWindowSpec> Windows,
    long Revision,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? NextRunAt,
    bool Synchronized,
    string? SynchronizationErrorCode);

public sealed record UpdateReportScheduleCommand(
    bool Enabled,
    int DayOfMonth,
    ShortMonthStrategy? ShortMonthStrategy,
    string LocalTime,
    string Timezone,
    IReadOnlyList<ReportWindowSpec>? Windows,
    long ExpectedRevision);

public sealed record RetryReportTaskCommand(Guid RunId, bool ConfirmOutcomeUnknown);

public sealed record ReportTaskRunPage(
    IReadOnlyList<ReportTaskRunDocument> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

public sealed record ReportTaskRunDocument(
    Guid Id,
    ReportRunTrigger Trigger,
    ReportRunStatus Status,
    Guid? ReportId,
    DateOnly? PeriodEnd,
    string? Timezone,
    long? ScheduleRevision,
    Guid? RetryOfRunId,
    int Attempt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CollectingAt,
    DateTimeOffset? RenderingAt,
    DateTimeOffset? DeliveringAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode,
    string? ErrorMessage,
    int DeliveryCount,
    int SucceededDeliveryCount,
    int FailedDeliveryCount,
    bool HasOutcomeUnknown,
    bool CanRetry);

public sealed class ReportScheduleConflictException(long expectedRevision, long actualRevision)
    : Exception($"Report schedule revision {expectedRevision} is stale; current revision is {actualRevision}.");

public sealed class ReportScheduleSynchronizationException(string errorCode)
    : Exception($"The report schedule could not be synchronized: {errorCode}.")
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class ReportTaskRunNotFoundException(Guid runId)
    : Exception($"The report task run {runId} does not exist.");

public sealed class ReportTaskRunNotRetryableException(Guid runId)
    : Exception($"The report task run {runId} cannot be retried.");

public sealed class ReportTaskOutcomeUnknownConfirmationRequiredException(Guid runId)
    : Exception($"The report task run {runId} has deliveries with an unknown outcome.");
