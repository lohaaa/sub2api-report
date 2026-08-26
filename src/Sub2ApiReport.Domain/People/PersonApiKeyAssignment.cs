using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Domain.People;

public sealed class PersonApiKeyAssignment
{
    private PersonApiKeyAssignment()
    {
    }

    public Guid Id { get; private init; }

    public Guid PersonId { get; private init; }

    public Person Person { get; private init; } = null!;

    public Guid ExternalApiKeyId { get; private init; }

    public ExternalApiKey ExternalApiKey { get; private init; } = null!;

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public long Revision { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PersonApiKeyAssignment Create(
        Guid personId,
        Guid externalApiKeyId,
        DateOnly validFrom,
        DateOnly? validTo,
        DateTimeOffset createdAt)
    {
        ValidateIds(personId, externalApiKeyId);
        ValidateRange(validFrom, validTo);
        return new PersonApiKeyAssignment
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            ExternalApiKeyId = externalApiKeyId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public void Update(
        DateOnly validFrom,
        DateOnly? validTo,
        DateTimeOffset updatedAt)
    {
        ValidateRange(validFrom, validTo);
        ValidFrom = validFrom;
        ValidTo = validTo;
        UpdatedAt = updatedAt;
        Revision++;
    }

    public bool IsEffectiveOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || ValidTo >= date);

    public bool Overlaps(DateOnly validFrom, DateOnly? validTo)
    {
        var thisEnd = ValidTo ?? DateOnly.MaxValue;
        var otherEnd = validTo ?? DateOnly.MaxValue;
        return ValidFrom <= otherEnd && validFrom <= thisEnd;
    }

    private static void ValidateIds(Guid personId, Guid externalApiKeyId)
    {
        if (personId == Guid.Empty)
        {
            throw new ArgumentException("The person identifier is required.", nameof(personId));
        }

        if (externalApiKeyId == Guid.Empty)
        {
            throw new ArgumentException("The API Key identifier is required.", nameof(externalApiKeyId));
        }
    }

    private static void ValidateRange(DateOnly validFrom, DateOnly? validTo)
    {
        if (validTo < validFrom)
        {
            throw new ArgumentException("The valid-to date cannot be earlier than valid-from.", nameof(validTo));
        }
    }
}
