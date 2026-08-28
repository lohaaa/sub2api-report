using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Api.Models;

/// <summary>Specifies the cutoff date and optional statistics windows for a manually generated report.</summary>
public sealed record GenerateReportRequest(
    DateOnly? CutoffDate,
    IReadOnlyList<ReportWindowSpecRequest>? Windows);

/// <summary>Defines one requested statistics window.</summary>
public sealed record ReportWindowSpecRequest(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays,
    DayOfWeek? WeekStartsOn,
    DateOnly? CustomStartDate,
    DateOnly? CustomEndDate);

/// <summary>Represents one page of immutable report snapshots.</summary>
public sealed record ReportPageResponse(
    IReadOnlyList<ReportListItemResponse> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

/// <summary>Represents one report in the report list.</summary>
public sealed record ReportListItemResponse(
    Guid Id,
    int SchemaVersion,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateOnly CutoffDate,
    string Timezone,
    DateTimeOffset GeneratedAt,
    int UserCount,
    int KeyCount,
    int FailedRangeCount,
    string SevenDayActualCost,
    string ThirtyDayActualCost,
    IReadOnlyList<ReportWindowListSummaryResponse> Windows);

/// <summary>Represents one compact window summary in a report list item.</summary>
public sealed record ReportWindowListSummaryResponse(
    string Key,
    string Label,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    int DayCount,
    string TotalActualCost);

/// <summary>Represents an immutable canonical report snapshot.</summary>
public sealed record ReportDetailResponse(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    IReadOnlyList<ReportWindowResponse> Windows,
    IReadOnlyList<ReportWindowMetricsResponse> WindowTotals,
    IReadOnlyList<ReportUserUsageResponse> Users,
    IReadOnlyList<ReportKeyUsageResponse> Keys,
    ReportDiagnosticsResponse Diagnostics);

/// <summary>Represents one resolved complete-natural-day report window with an exclusive end date.</summary>
public sealed record ReportWindowResponse(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays,
    DayOfWeek? WeekStartsOn,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    int DayCount,
    string Label);

/// <summary>Associates aggregate metrics with a report window.</summary>
public sealed record ReportWindowMetricsResponse(
    string WindowKey,
    ReportUsageMetricsResponse Metrics);

/// <summary>Represents aggregate usage metrics.</summary>
public sealed record ReportUsageMetricsResponse(
    string TotalRequests,
    string TotalInputTokens,
    string TotalOutputTokens,
    string TotalCacheTokens,
    string TotalCacheCreationTokens,
    string TotalCacheReadTokens,
    string TotalTokens,
    string TotalCost,
    string TotalActualCost,
    string AverageDurationMs);

/// <summary>Represents usage attributed to one Sub2API user.</summary>
public sealed record ReportUserUsageResponse(
    Guid UserId,
    long ExternalUserId,
    string? Username,
    string Email,
    int KeyCount,
    IReadOnlyList<ReportWindowMetricsResponse> Windows);

/// <summary>Represents one API Key and its report-window usage.</summary>
public sealed record ReportKeyUsageResponse(
    Guid KeyId,
    string ExternalId,
    string? SourceUserId,
    string? SourceUserEmail,
    string Name,
    string Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RetiredAt,
    IReadOnlyList<ReportWindowMetricsResponse> Windows);

/// <summary>Represents completeness diagnostics for a report.</summary>
public sealed record ReportDiagnosticsResponse(
    IReadOnlyList<ReportRangeFailureResponse> FailedRanges);

/// <summary>Represents one collection range that failed while generating the report.</summary>
public sealed record ReportRangeFailureResponse(
    long ExternalUserId,
    string UserEmail,
    long ExternalKeyId,
    string KeyName,
    string WindowKey,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    Sub2ApiFailureKind? FailureKind,
    string? ErrorCode);

/// <summary>Represents one page of report generation runs.</summary>
public sealed record ReportGenerationRunPageResponse(
    IReadOnlyList<ReportGenerationRunItemResponse> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

/// <summary>Represents one report generation attempt, including refresh failures.</summary>
public sealed record ReportGenerationRunItemResponse(
    Guid Id,
    ReportTrigger Trigger,
    ReportGenerationStatus Status,
    string? Stage,
    string? ErrorCode,
    string? ErrorMessage,
    long ConnectionRevision,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? ReportSnapshotId);
