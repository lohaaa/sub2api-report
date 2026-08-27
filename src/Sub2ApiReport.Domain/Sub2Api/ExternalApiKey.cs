namespace Sub2ApiReport.Domain.Sub2Api;

public sealed class ExternalApiKey
{
    private ExternalApiKey()
    {
    }

    public Guid Id { get; private init; }

    public long ExternalId { get; private init; }

    public Guid? Sub2ApiUserId { get; private set; }

    public Sub2ApiUser? Sub2ApiUser { get; private set; }

    public string NameSnapshot { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public long? GroupId { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public static ExternalApiKey Create(
        Guid sub2ApiUserId,
        long externalId,
        string name,
        string status,
        long? groupId,
        DateTimeOffset? lastUsedAt,
        DateTimeOffset seenAt) => new()
        {
            Id = Guid.NewGuid(),
            Sub2ApiUserId = ValidateUserId(sub2ApiUserId),
            ExternalId = ValidateExternalId(externalId),
            NameSnapshot = ValidateText(name, 200, nameof(name)),
            Status = ValidateText(status, 32, nameof(status)).ToLowerInvariant(),
            GroupId = ValidateOptionalPositiveId(groupId, nameof(groupId)),
            LastUsedAt = lastUsedAt,
            LastSeenAt = seenAt,
        };

    public void AssignUser(Guid userId)
    {
        Sub2ApiUserId = ValidateUserId(userId);
    }

    public bool ApplySnapshot(
        string name,
        string status,
        long? groupId,
        DateTimeOffset? lastUsedAt,
        DateTimeOffset seenAt)
    {
        var normalizedName = ValidateText(name, 200, nameof(name));
        var normalizedStatus = ValidateText(status, 32, nameof(status)).ToLowerInvariant();
        var normalizedGroupId = ValidateOptionalPositiveId(groupId, nameof(groupId));
        var changed = NameSnapshot != normalizedName
            || Status != normalizedStatus
            || GroupId != normalizedGroupId
            || LastUsedAt != lastUsedAt
            || RetiredAt is not null;

        NameSnapshot = normalizedName;
        Status = normalizedStatus;
        GroupId = normalizedGroupId;
        LastUsedAt = lastUsedAt;
        LastSeenAt = seenAt;
        RetiredAt = null;
        return changed;
    }

    public bool MarkRetired(DateTimeOffset retiredAt)
    {
        if (RetiredAt is not null)
        {
            return false;
        }

        RetiredAt = retiredAt;
        return true;
    }

    private static Guid ValidateUserId(Guid value) => value != Guid.Empty
        ? value
        : throw new ArgumentException("The Sub2API user identifier is required.", nameof(value));

    private static long ValidateExternalId(long value) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), "The external identifier must be positive.");

    private static long? ValidateOptionalPositiveId(long? value, string parameterName) => value is null
        ? null
        : value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "The identifier must be positive.");

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
