using System.ComponentModel.DataAnnotations;

namespace Sub2ApiReport.Api.Models;

/// <summary>Represents the single Sub2API connection without exposing its Admin API Key.</summary>
public sealed record Sub2ApiConnectionResponse(
    bool Configured,
    string? BaseUrl,
    bool HasAdminApiKey,
    string? AdminApiKeyMask,
    string? UserId,
    string? CodexGroupId,
    long Revision,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestCode,
    DateTimeOffset? LastSynchronizedAt,
    int? LastSynchronizedKeyCount);

/// <summary>Creates or replaces the mutable Sub2API connection settings.</summary>
public sealed record SaveSub2ApiConnectionRequest
{
    [Required, StringLength(2048, MinimumLength = 8)]
    public required string BaseUrl { get; init; }

    [StringLength(4096, MinimumLength = 8)]
    public string? AdminApiKey { get; init; }

    public bool ClearAdminApiKey { get; init; }

    [Required, RegularExpression("^[1-9][0-9]{0,18}$")]
    public required string UserId { get; init; }

    [RegularExpression("^[1-9][0-9]{0,18}$")]
    public string? CodexGroupId { get; init; }

    [Range(0, long.MaxValue)]
    public required long Revision { get; init; }
}

/// <summary>Reports the result of a Sub2API connection probe.</summary>
public sealed record Sub2ApiConnectionTestResponse(
    bool Succeeded,
    string Code,
    string Message,
    long? AvailableKeyCount,
    DateTimeOffset TestedAt);
