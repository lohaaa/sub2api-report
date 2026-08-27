using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Api.Models;

/// <summary>Represents the synchronized Sub2API user directory and report scope.</summary>
public sealed record Sub2ApiUserScopeResponse(
    string ScopeMode,
    IReadOnlyList<Sub2ApiUserResponse> Users,
    long ConnectionRevision,
    DateTimeOffset? LastSynchronizedAt);

/// <summary>Represents one upstream Sub2API user.</summary>
public sealed record Sub2ApiUserResponse(
    Guid Id,
    string ExternalId,
    string Email,
    string? Username,
    string Status,
    bool IsSelected,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RetiredAt);

/// <summary>Updates the explicit report user scope.</summary>
public sealed record UpdateSub2ApiUserScopeRequest(
    Sub2ApiUserScopeMode Mode,
    IReadOnlyList<Guid> SelectedUserIds,
    long Revision);

/// <summary>Reports a completed upstream user synchronization.</summary>
public sealed record Sub2ApiUserSynchronizationResponse(
    int Added,
    int Updated,
    int Retired,
    int Total,
    DateTimeOffset SynchronizedAt,
    long ConfigurationRevision);
