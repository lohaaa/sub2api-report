using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Domain.Reports;

public enum DeliveryStatus
{
    Pending,
    Sending,
    Succeeded,
    Failed,
}

public sealed class DeliveryRecord
{
    public const int ErrorCodeMaxLength = 64;
    public const int ErrorMessageMaxLength = 512;
    public const int PayloadHashLength = 64;

    private DeliveryRecord()
    {
    }

    public Guid Id { get; private init; }

    public Guid RunId { get; private init; }

    public ReportRun Run { get; private init; } = null!;

    public Guid ChannelId { get; private init; }

    public NotificationChannelType ChannelType { get; private init; }

    public string ChannelName { get; private init; } = string.Empty;

    public DeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? PayloadHash { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public List<DeliveryPart> Parts { get; private init; } = [];


    public ReportDownloadGrant? DownloadGrant { get; private init; }
    public static DeliveryRecord Create(
        Guid channelId,
        NotificationChannelType channelType,
        string channelName,
        string payloadHash,
        IReadOnlyList<DeliveryPart> parts,
        Guid? id = null,
        Guid? runId = null)
    {
        if (channelId == Guid.Empty)
        {
            throw new ArgumentException("The channel identifier is required.", nameof(channelId));
        }
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The delivery identifier cannot be empty.", nameof(id));
        }


        ArgumentException.ThrowIfNullOrWhiteSpace(channelName, nameof(channelName));
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash, nameof(payloadHash));
        if (parts.Count == 0)
        {
            throw new ArgumentException("At least one delivery part is required.", nameof(parts));
        }

        var record = new DeliveryRecord
        {
            Id = id ?? Guid.NewGuid(),
            RunId = runId ?? Guid.Empty,
            ChannelId = channelId,
            ChannelType = channelType,
            ChannelName = channelName,
            Status = DeliveryStatus.Pending,
            PayloadHash = payloadHash,
        };
        foreach (var part in parts)
        {
            record.Parts.Add(part);
        }

        return record;
    }

    public void MarkSending()
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Failed))
        {
            throw new InvalidOperationException(
                "Only pending or failed deliveries can be marked as sending.");
        }

        Status = DeliveryStatus.Sending;
        ErrorCode = null;
        ErrorMessage = null;
        SentAt = null;
        Attempts++;
    }

    public void MarkSucceeded(DateTimeOffset sentAt)
    {
        if (Status != DeliveryStatus.Sending)
        {
            throw new InvalidOperationException("Only sending deliveries can succeed.");
        }

        Status = DeliveryStatus.Succeeded;
        ErrorCode = null;
        ErrorMessage = null;
        SentAt = sentAt;
    }

    public void MarkFailed(string errorCode, string? errorMessage)
    {
        if (Status != DeliveryStatus.Sending)
        {
            throw new InvalidOperationException("Only sending deliveries can fail.");
        }

        Status = DeliveryStatus.Failed;
        ErrorCode = ValidateCode(errorCode);
        ErrorMessage = errorMessage is null
            ? null
            : errorMessage.Length <= ErrorMessageMaxLength
                ? errorMessage
                : errorMessage[..ErrorMessageMaxLength];
        SentAt = null;
    }

    public void ResetForRetry(string payloadHash)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Failed))
        {
            throw new InvalidOperationException("Only failed deliveries can be retried.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash, nameof(payloadHash));
        Status = DeliveryStatus.Pending;
        PayloadHash = payloadHash;
        ErrorCode = null;
        ErrorMessage = null;
        SentAt = null;
    }

    private static string ValidateCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = value.Trim();
        return normalized.Length <= ErrorCodeMaxLength
            ? normalized
            : throw new ArgumentException("The error code cannot exceed its maximum length.", nameof(value));
    }
}
