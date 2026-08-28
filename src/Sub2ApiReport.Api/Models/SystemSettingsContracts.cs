using System.ComponentModel.DataAnnotations;

namespace Sub2ApiReport.Api.Models;

/// <summary>Represents the mutable system settings stored in SQLite.</summary>
public sealed record SystemSettingsResponse(
    string Timezone,
    string ReleaseChannel,
    string LogLevel,
    int ReportConcurrency,
    int ReportRetentionMonths,
    int BackupRetentionCount,
    string? ReportExternalBaseUrl,
    int ReportDownloadLinkHours,
    int? ReportDownloadMaxDownloads,
    long Revision,
    DateTimeOffset? UpdatedAt);

/// <summary>Replaces the mutable system settings using optimistic concurrency.</summary>
public sealed record UpdateSystemSettingsRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Timezone { get; init; }

    [Required, StringLength(32, MinimumLength = 1)]
    public required string ReleaseChannel { get; init; }

    [Required, StringLength(16, MinimumLength = 3)]
    public required string LogLevel { get; init; }

    [Range(1, 10)]
    public required int ReportConcurrency { get; init; }

    [Range(1, 120)]
    public required int ReportRetentionMonths { get; init; }

    [Range(1, 100)]
    public required int BackupRetentionCount { get; init; }

    [StringLength(2048)]
    public string? ReportExternalBaseUrl { get; init; }

    [Range(1, 720)]
    public required int ReportDownloadLinkHours { get; init; }

    [Range(1, 10_000)]
    public int? ReportDownloadMaxDownloads { get; init; }

    [Range(1, long.MaxValue)]
    public required long Revision { get; init; }
}
