using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Application.Reports;

public interface IReportService
{
    Task<ReportDocument> GenerateDryRunAsync(
        GenerateReportCommand command,
        CancellationToken cancellationToken);

    Task<ReportDocument> GenerateTaskReportAsync(
        GenerateTaskReportCommand command,
        CancellationToken cancellationToken);

    Task<ReportPage> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ReportDocument?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportXlsx?> GetXlsxAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportGenerationRunPage> GetGenerationRunsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed record GenerateReportCommand(
    DateOnly? CutoffDate,
    IReadOnlyList<ReportWindowSpec>? Windows);

public sealed record GenerateTaskReportCommand(
    Guid ReportRunId,
    DateOnly CutoffDate,
    string Timezone,
    IReadOnlyList<ResolvedReportWindow> Windows,
    ReportTrigger Trigger);

public sealed record ReportXlsx(byte[] Content, string FileName);

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
    int UserCount,
    int KeyCount,
    int FailedRangeCount,
    decimal SevenDayActualCost,
    decimal ThirtyDayActualCost,
    string? WindowSummaryJson);

public sealed record ReportGenerationRunPage(
    IReadOnlyList<ReportGenerationRunItem> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages);

public sealed record ReportGenerationRunItem(
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

/// <summary>Describes one ordered report window of a canonical snapshot.</summary>
public sealed record ReportWindowDescriptor(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays,
    DayOfWeek? WeekStartsOn,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    int DayCount,
    string Label);

/// <summary>Links aggregate metrics to one window key.</summary>
public sealed record ReportWindowMetrics(string WindowKey, ReportUsageMetrics Metrics);

public sealed record ReportDocument(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    IReadOnlyList<ReportWindowDescriptor> Windows,
    IReadOnlyList<ReportWindowMetrics> WindowTotals,
    IReadOnlyList<ReportUserUsage> Users,
    IReadOnlyList<ReportKeyUsage> Keys,
    ReportDiagnostics Diagnostics)
{
    /// <summary>Gets the total actual cost of the seven-day rolling window when present.</summary>
    public decimal SevenDayActualCost =>
        WindowTotals.FirstOrDefault(item => item.WindowKey == ReportWindows.RollingSevenDaysKey)
            ?.Metrics.TotalActualCost ?? 0m;

    /// <summary>Gets the total actual cost of the thirty-day rolling window when present.</summary>
    public decimal ThirtyDayActualCost =>
        WindowTotals.FirstOrDefault(item => item.WindowKey == ReportWindows.RollingThirtyDaysKey)
            ?.Metrics.TotalActualCost ?? 0m;
}

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

public sealed record ReportUserUsage(
    Guid UserId,
    long ExternalUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Username,
    string Email,
    int KeyCount,
    IReadOnlyList<ReportWindowMetrics> Windows);

public sealed record ReportKeyUsage(
    Guid KeyId,
    string ExternalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SourceUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceUserEmail,
    string Name,
    string Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RetiredAt,
    IReadOnlyList<ReportWindowMetrics> Windows);

public sealed record ReportDiagnostics(
    IReadOnlyList<ReportRangeFailure> FailedRanges);

public sealed record ReportRangeFailure(
    long ExternalUserId,
    string UserEmail,
    long ExternalKeyId,
    string KeyName,
    string WindowKey,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    Sub2ApiFailureKind? FailureKind,
    string? ErrorCode);

public sealed class ReportGenerationPreconditionException : InvalidOperationException
{
    public ReportGenerationPreconditionException(string message)
        : base(message)
    {
    }

    public ReportGenerationPreconditionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
