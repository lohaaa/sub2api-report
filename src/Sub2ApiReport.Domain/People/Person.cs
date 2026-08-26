namespace Sub2ApiReport.Domain.People;

public sealed class Person
{
    private Person()
    {
    }

    public Guid Id { get; private init; }

    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public long Revision { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Person Create(
        string code,
        string displayName,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            Code = ValidateCode(code),
            DisplayName = ValidateDisplayName(displayName),
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Update(
        string code,
        string displayName,
        bool isActive,
        DateTimeOffset updatedAt)
    {
        Code = ValidateCode(code);
        DisplayName = ValidateDisplayName(displayName);
        IsActive = isActive;
        UpdatedAt = updatedAt;
        Revision++;
    }

    public void Deactivate(DateTimeOffset updatedAt)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = updatedAt;
        Revision++;
    }

    private static string ValidateCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 64
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "The person code must use 1 to 64 ASCII letters, digits, dots, underscores, or hyphens.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidateDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        return normalized.Length <= 200
            ? normalized
            : throw new ArgumentException("The display name cannot exceed 200 characters.", nameof(value));
    }
}
