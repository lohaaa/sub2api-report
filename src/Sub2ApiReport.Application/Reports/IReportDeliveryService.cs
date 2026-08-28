using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Application.Reports;

public interface IReportDeliveryService
{
    Task<DeliveryRunDocument> DeliverAsync(
        DeliverReportCommand command,
        CancellationToken cancellationToken);

    Task<DeliveryRunDocument> RetryAsync(
        RetryDeliveryCommand command,
        CancellationToken cancellationToken);

    Task<DeliveryRunDocument> DeliverTaskAsync(
        DeliverReportTaskCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryRunDocument>> GetRunsAsync(
        Guid reportId,
        CancellationToken cancellationToken);
}

public sealed record DeliverReportCommand(
    Guid ReportId,
    IReadOnlyList<Guid> ChannelIds,
    bool ConfirmPartial);

public sealed record RetryDeliveryCommand(Guid ReportId, Guid RunId);

public sealed record DeliverReportTaskCommand(Guid RunId, bool Recovering);

public sealed record DeliveryRunDocument(
    Guid Id,
    Guid ReportId,
    ReportRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<DeliveryDocument> Deliveries);

public sealed record DeliveryDocument(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    DeliveryStatus Status,
    int Attempts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? SentAt,
    IReadOnlyList<DeliveryPartDocument> Parts,
    ReportDownloadGrantDocument? DownloadGrant = null);

public sealed record DeliveryPartDocument(
    int Index,
    int Count,
    DeliveryPartStatus Status,
    int Attempts,
    string? ErrorCode,
    DateTimeOffset? SentAt);

public sealed class ReportDeliveryPreconditionException(string message)
    : InvalidOperationException(message);

public sealed class ReportRunNotFoundException(Guid reportId, Guid runId)
    : InvalidOperationException($"The report {reportId} has no delivery run {runId}.");

public sealed class ReportRunNotRetryableException(Guid runId)
    : InvalidOperationException($"The delivery run {runId} has no failed channels to retry.");

public sealed class ReportNotFoundException(Guid reportId)
    : InvalidOperationException($"The report {reportId} does not exist.");
