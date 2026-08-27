namespace Sub2ApiReport.Api.Models;

/// <summary>Represents a page of synchronized Sub2API Key snapshots and mapping diagnostics.</summary>
public sealed record ApiKeyInventoryPageResponse(
    IReadOnlyList<ApiKeyInventoryItemResponse> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages,
    ApiKeyInventoryDiagnosticsResponse Diagnostics,
    DateTimeOffset? LastSynchronizedAt);

/// <summary>Represents one synchronized Key without exposing the business Key value.</summary>
public sealed record ApiKeyInventoryItemResponse(
    Guid Id,
    string ExternalId,
    string? SourceUserId,
    string? SourceUserEmail,
    string Name,
    string Status,
    string? GroupId,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RetiredAt);

/// <summary>Reports inventory consistency counts for the synchronized Key inventory.</summary>
public sealed record ApiKeyInventoryDiagnosticsResponse(
    int RetiredKeys);

/// <summary>Reports the result of a completed all-or-nothing Sub2API Key synchronization.</summary>
public sealed record KeySynchronizationResponse(
    int Added,
    int Updated,
    int Retired,
    int Total,
    DateTimeOffset SynchronizedAt,
    long ConfigurationRevision);
