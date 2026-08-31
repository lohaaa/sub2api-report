namespace Sub2ApiReport.Domain.Reports;

public enum DeliveryPartStatus
{
    Pending,
    Succeeded,
    Failed,
}

public sealed class DeliveryPart
{
    public const int ErrorCodeMaxLength = 64;
    public const int ErrorMessageMaxLength = 512;
    public const int PayloadHashLength = 64;

    private DeliveryPart()
    {
    }

    public Guid Id { get; private init; }

    public Guid DeliveryId { get; private init; }

    public DeliveryRecord Delivery { get; private init; } = null!;

    public int PartIndex { get; private init; }

    public int PartCount { get; private set; }

    public string PayloadHash { get; private set; } = string.Empty;

    public DeliveryPartStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static DeliveryPart Create(
        int partIndex,
        int partCount,
        string payloadHash,
        Guid? deliveryId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(partIndex, nameof(partIndex));
        ArgumentOutOfRangeException.ThrowIfLessThan(partCount, 1, nameof(partCount));
        if (partIndex >= partCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partIndex),
                "The part index must be smaller than the part count.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash, nameof(payloadHash));
        return new DeliveryPart
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId ?? Guid.Empty,
            PartIndex = partIndex,
            PartCount = partCount,
            PayloadHash = payloadHash,
            Status = DeliveryPartStatus.Pending,
        };
    }

    public void MarkSucceeded(DateTimeOffset sentAt)
    {
        if (Status != DeliveryPartStatus.Pending)
        {
            throw new InvalidOperationException("Only pending parts can succeed.");
        }

        Status = DeliveryPartStatus.Succeeded;
        ErrorCode = null;
        ErrorMessage = null;
        SentAt = sentAt;
    }

    public void MarkFailed(string errorCode, string? errorMessage)
    {
        if (Status != DeliveryPartStatus.Pending)
        {
            throw new InvalidOperationException("Only pending parts can fail.");
        }

        Status = DeliveryPartStatus.Failed;
        ErrorCode = Validate(errorCode, ErrorCodeMaxLength);
        ErrorMessage = errorMessage is null
            ? null
            : errorMessage.Length <= ErrorMessageMaxLength
                ? errorMessage
                : errorMessage[..ErrorMessageMaxLength];
        SentAt = null;
    }

    public void RebindForRetry(string payloadHash, int partCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash, nameof(payloadHash));
        ArgumentOutOfRangeException.ThrowIfLessThan(partCount, 1, nameof(partCount));

        PayloadHash = payloadHash;
        PartCount = partCount;
        Status = DeliveryPartStatus.Pending;
        ErrorCode = null;
        ErrorMessage = null;
        SentAt = null;
    }

    private static string Validate(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException("The error code cannot exceed its maximum length.", nameof(value));
    }
}
