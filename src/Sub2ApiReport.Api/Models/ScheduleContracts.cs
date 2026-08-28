using System.ComponentModel.DataAnnotations;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Api.Models;

/// <summary>Updates the singleton monthly report schedule.</summary>
public sealed record UpdateReportScheduleRequest
{
    /// <summary>Gets whether automatic monthly execution is enabled.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the monthly day, limited to 1 through 28.</summary>
    [Range(1, 28)]
    public required int DayOfMonth { get; init; }

    /// <summary>Gets the wall-clock execution time in HH:mm format.</summary>
    [Required, RegularExpression("^(?:[01]\\d|2[0-3]):[0-5]\\d$")]
    public required string LocalTime { get; init; }

    /// <summary>Gets the IANA time zone identifier.</summary>
    [Required, MaxLength(100)]
    public required string Timezone { get; init; }

    /// <summary>Gets the configured recurring report windows; null restores the defaults.</summary>
    public IReadOnlyList<ReportWindowSpecRequest>? Windows { get; init; }

    /// <summary>Gets the revision observed by the caller.</summary>
    [Range(1, long.MaxValue)]
    public required long Revision { get; init; }
}

/// <summary>Requests a retry of a terminal report task execution.</summary>
public sealed record RetryReportTaskRequest
{
    /// <summary>Gets whether deliveries with an unknown result may be retried explicitly.</summary>
    public required bool ConfirmOutcomeUnknown { get; init; }
}

/// <summary>Represents one configured recurring report window.</summary>
public sealed record ReportWindowSpecResponse(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays,
    DayOfWeek? WeekStartsOn,
    DateOnly? CustomStartDate,
    DateOnly? CustomEndDate);

/// <summary>Represents the effective monthly report schedule.</summary>
public sealed record ReportScheduleResponse(
    bool Enabled,
    int DayOfMonth,
    string LocalTime,
    string Timezone,
    IReadOnlyList<ReportWindowSpecResponse> Windows,
    long Revision,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? NextRunAt,
    bool Synchronized,
    string? SynchronizationErrorCode);

/// <summary>Represents one page of report task execution records.</summary>
public sealed record ReportTaskRunPageResponse(
    IReadOnlyList<ReportTaskRunResponse> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

/// <summary>Represents one normalized report task execution and its retry relationship.</summary>
public sealed record ReportTaskRunResponse(
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
