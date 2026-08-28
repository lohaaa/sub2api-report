namespace Sub2ApiReport.Application.Reports;

public interface IReportDownloadService
{
    Task<ReportDownloadLink?> PrepareLinkAsync(
        Guid reportId,
        Guid deliveryId,
        CancellationToken cancellationToken);

    Task ActivateAsync(Guid deliveryId, CancellationToken cancellationToken);

    Task<ReportDownloadAttempt> DownloadAsync(
        string token,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Guid reportId,
        Guid grantId,
        CancellationToken cancellationToken);
}

public sealed record ReportDownloadLink(
    Guid GrantId,
    string Url,
    int LifetimeHours,
    int? MaxDownloads);

public enum ReportDownloadAttemptStatus
{
    Available,
    Invalid,
    Inactive,
}

public sealed record ReportDownloadAttempt(
    ReportDownloadAttemptStatus Status,
    byte[]? Content = null,
    string? FileName = null);

public sealed record ReportDownloadGrantDocument(
    Guid Id,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    int DownloadCount,
    int? MaxDownloads,
    DateTimeOffset? LastDownloadedAt);
