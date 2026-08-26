using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Api.Models;

/// <summary>Specifies the cutoff date for a manually generated report.</summary>
public sealed record GenerateReportRequest(DateOnly? CutoffDate);

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
    int PersonCount,
    int KeyCount,
    int FailedSegmentCount,
    int UnassignedSegmentCount,
    string SevenDayActualCost,
    string ThirtyDayActualCost);

/// <summary>Represents an immutable canonical report snapshot.</summary>
public sealed record ReportDetailResponse(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    ReportWindowResponse SevenDayWindow,
    ReportWindowResponse ThirtyDayWindow,
    ReportUsageMetricsResponse SevenDayTotal,
    ReportUsageMetricsResponse ThirtyDayTotal,
    IReadOnlyList<ReportPersonUsageResponse> People,
    IReadOnlyList<ReportKeyUsageResponse> Keys,
    ReportDiagnosticsResponse Diagnostics);

/// <summary>Represents an inclusive complete-natural-day report window.</summary>
public sealed record ReportWindowResponse(int Days, DateOnly StartDate, DateOnly EndDate);

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

/// <summary>Represents usage attributed to one person.</summary>
public sealed record ReportPersonUsageResponse(
    Guid PersonId,
    string Code,
    string DisplayName,
    int KeyCount,
    ReportUsageMetricsResponse SevenDay,
    ReportUsageMetricsResponse ThirtyDay);

/// <summary>Represents one API Key and its report-window usage.</summary>
public sealed record ReportKeyUsageResponse(
    Guid KeyId,
    string ExternalId,
    string Name,
    string Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RetiredAt,
    ReportUsageMetricsResponse SevenDay,
    ReportUsageMetricsResponse ThirtyDay,
    IReadOnlyList<ReportKeySegmentResponse> Segments);

/// <summary>Represents one atomic ownership and collection segment for an API Key.</summary>
public sealed record ReportKeySegmentResponse(
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? PersonId,
    string? PersonCode,
    string? PersonDisplayName,
    ReportUsageMetricsResponse? Metrics,
    Sub2ApiFailureKind? FailureKind,
    string? DiagnosticCode);

/// <summary>Represents completeness diagnostics for a report.</summary>
public sealed record ReportDiagnosticsResponse(
    IReadOnlyList<ReportSegmentDiagnosticResponse> FailedSegments,
    IReadOnlyList<ReportSegmentDiagnosticResponse> UnassignedSegments,
    IReadOnlyList<ReportSegmentDiagnosticResponse> ConflictingSegments,
    IReadOnlyList<string> ZeroUsageKeyIds);

/// <summary>Represents one report segment requiring attention.</summary>
public sealed record ReportSegmentDiagnosticResponse(
    string ExternalKeyId,
    string KeyName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Code,
    Sub2ApiFailureKind? FailureKind);
