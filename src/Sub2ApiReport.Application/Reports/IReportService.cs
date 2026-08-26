using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Application.Reports;

public interface IReportService
{
    Task<ReportDocument> GenerateDryRunAsync(
        GenerateReportCommand command,
        CancellationToken cancellationToken);

    Task<ReportPage> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ReportDocument?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportCsv?> GetCsvAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record GenerateReportCommand(DateOnly? CutoffDate);

public sealed record ReportCsv(byte[] Content, string FileName);

public sealed record ReportPage(
    IReadOnlyList<ReportListItem> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

public sealed record ReportListItem(
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
    decimal SevenDayActualCost,
    decimal ThirtyDayActualCost);

public sealed record ReportDocument(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    ReportWindow SevenDayWindow,
    ReportWindow ThirtyDayWindow,
    ReportUsageMetrics SevenDayTotal,
    ReportUsageMetrics ThirtyDayTotal,
    IReadOnlyList<ReportPersonUsage> People,
    IReadOnlyList<ReportKeyUsage> Keys,
    ReportDiagnostics Diagnostics);

public sealed record ReportWindow(int Days, DateOnly StartDate, DateOnly EndDate);

public sealed record ReportUsageMetrics(
    long TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheTokens,
    long TotalCacheCreationTokens,
    long TotalCacheReadTokens,
    long TotalTokens,
    decimal TotalCost,
    decimal TotalActualCost,
    decimal AverageDurationMs);

public sealed record ReportPersonUsage(
    Guid PersonId,
    string Code,
    string DisplayName,
    int KeyCount,
    ReportUsageMetrics SevenDay,
    ReportUsageMetrics ThirtyDay);

public sealed record ReportKeyUsage(
    Guid KeyId,
    string ExternalId,
    string Name,
    string Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RetiredAt,
    ReportUsageMetrics SevenDay,
    ReportUsageMetrics ThirtyDay,
    IReadOnlyList<ReportKeySegment> Segments);

public sealed record ReportKeySegment(
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? PersonId,
    string? PersonCode,
    string? PersonDisplayName,
    ReportUsageMetrics? Metrics,
    Sub2ApiFailureKind? FailureKind,
    string? DiagnosticCode);

public sealed record ReportDiagnostics(
    IReadOnlyList<ReportSegmentDiagnostic> FailedSegments,
    IReadOnlyList<ReportSegmentDiagnostic> UnassignedSegments,
    IReadOnlyList<ReportSegmentDiagnostic> ConflictingSegments,
    IReadOnlyList<string> ZeroUsageKeyIds);

public sealed record ReportSegmentDiagnostic(
    string ExternalKeyId,
    string KeyName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Code,
    Sub2ApiFailureKind? FailureKind);

public sealed class ReportGenerationPreconditionException(string message)
    : InvalidOperationException(message);
