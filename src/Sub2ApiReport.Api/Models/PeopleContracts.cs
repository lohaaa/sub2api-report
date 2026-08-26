using System.ComponentModel.DataAnnotations;

namespace Sub2ApiReport.Api.Models;

/// <summary>Represents a person that owns one or more Sub2API Keys.</summary>
public sealed record PersonResponse(
    Guid Id,
    string Code,
    string DisplayName,
    bool IsActive,
    int CurrentApiKeyCount,
    int AssignmentCount,
    long Revision,
    DateTimeOffset UpdatedAt);

/// <summary>Creates a person record.</summary>
public sealed record CreatePersonRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public required string DisplayName { get; init; }
}

/// <summary>Replaces a person record using optimistic concurrency.</summary>
public sealed record UpdatePersonRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public required string DisplayName { get; init; }

    public required bool IsActive { get; init; }

    [Range(1, long.MaxValue)]
    public required long Revision { get; init; }
}

/// <summary>Represents a dated person-to-Key assignment.</summary>
public sealed record ApiKeyAssignmentResponse(
    Guid Id,
    Guid PersonId,
    string PersonCode,
    string PersonDisplayName,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    long Revision);

/// <summary>Creates a dated API Key assignment for a person.</summary>
public sealed record CreateApiKeyAssignmentRequest
{
    public required Guid ExternalApiKeyId { get; init; }

    public required DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }
}

/// <summary>Replaces the validity range of an API Key assignment.</summary>
public sealed record UpdateApiKeyAssignmentRequest
{
    public required DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }

    [Range(1, long.MaxValue)]
    public required long Revision { get; init; }
}
