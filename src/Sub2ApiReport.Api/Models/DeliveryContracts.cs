using System.ComponentModel.DataAnnotations;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Api.Models;

/// <summary>Requests delivery of a saved report to selected channels.</summary>
public sealed record DeliverReportRequest
{
    /// <summary>Gets the identifiers of the channels that should receive the report.</summary>
    [Required, MinLength(1)]
    public required IReadOnlyList<Guid> ChannelIds { get; init; }

    /// <summary>Gets whether a partial report may be delivered knowingly.</summary>
    public required bool ConfirmPartial { get; init; }
}

/// <summary>Represents one delivery run of a report.</summary>
public sealed record DeliveryRunResponse(
    Guid Id,
    Guid ReportId,
    ReportRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<DeliveryResponse> Deliveries);

/// <summary>Represents the per-channel delivery state of a run.</summary>
public sealed record DeliveryResponse(
    Guid Id,
    Guid ChannelId,
    string ChannelType,
    string ChannelName,
    DeliveryStatus Status,
    int Attempts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? SentAt,
    IReadOnlyList<DeliveryPartResponse> Parts,
    ReportDownloadGrantResponse? DownloadGrant);

/// <summary>Represents one message part of a channel delivery.</summary>
public sealed record DeliveryPartResponse(
    int Index,
    int Count,
    DeliveryPartStatus Status,
    int Attempts,
    string? ErrorCode,
    DateTimeOffset? SentAt);

/// <summary>Represents the revocable CSV download authorization included in an IM delivery.</summary>
public sealed record ReportDownloadGrantResponse(
    Guid Id,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    int DownloadCount,
    int? MaxDownloads,
    DateTimeOffset? LastDownloadedAt);
