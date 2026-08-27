namespace Sub2ApiReport.Domain.Sub2Api;

public enum Sub2ApiUserScopeMode
{
    SelectedUsers,
    AllActiveUsers,
}

public sealed class Sub2ApiUser
{
    private Sub2ApiUser()
    {
    }

    public Guid Id { get; private init; }

    public long ExternalId { get; private init; }

    public string EmailSnapshot { get; private set; } = string.Empty;

    public string? UsernameSnapshot { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public bool IsSelected { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public static Sub2ApiUser Create(
        long externalId,
        string email,
        string? username,
        string status,
        bool isSelected,
        DateTimeOffset seenAt) => new()
        {
            Id = Guid.NewGuid(),
            ExternalId = ValidateExternalId(externalId),
            EmailSnapshot = ValidateText(email, 320, nameof(email)),
            UsernameSnapshot = NormalizeOptionalText(username, 200, nameof(username)),
            Status = ValidateText(status, 32, nameof(status)).ToLowerInvariant(),
            IsSelected = isSelected,
            LastSeenAt = seenAt,
        };

    public bool ApplySnapshot(string email, string? username, string status, DateTimeOffset seenAt)
    {
        var normalizedEmail = ValidateText(email, 320, nameof(email));
        var normalizedUsername = NormalizeOptionalText(username, 200, nameof(username));
        var normalizedStatus = ValidateText(status, 32, nameof(status)).ToLowerInvariant();
        var changed = EmailSnapshot != normalizedEmail
            || UsernameSnapshot != normalizedUsername
            || Status != normalizedStatus
            || RetiredAt is not null;

        EmailSnapshot = normalizedEmail;
        UsernameSnapshot = normalizedUsername;
        Status = normalizedStatus;
        LastSeenAt = seenAt;
        RetiredAt = null;
        return changed;
    }

    public bool SetSelected(bool selected)
    {
        if (IsSelected == selected)
        {
            return false;
        }

        IsSelected = selected;
        return true;
    }

    public bool MarkRetired(DateTimeOffset retiredAt)
    {
        if (RetiredAt is not null)
        {
            return false;
        }

        RetiredAt = retiredAt;
        IsSelected = false;
        return true;
    }

    private static long ValidateExternalId(long value) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), "The external identifier must be positive.");

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : ValidateText(value, maximumLength, parameterName);
}
