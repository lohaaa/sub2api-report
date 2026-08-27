using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Application.Sub2Api;

public interface ISub2ApiUserService
{
    Task<Sub2ApiUserScopeSnapshot> GetAsync(CancellationToken cancellationToken);

    Task<Sub2ApiUserSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken);

    Task<Sub2ApiUserScopeSnapshot> UpdateScopeAsync(
        UpdateSub2ApiUserScopeCommand command,
        CancellationToken cancellationToken);
}

public sealed record Sub2ApiUserScopeSnapshot(
    Sub2ApiUserScopeMode ScopeMode,
    IReadOnlyList<Sub2ApiUserSnapshot> Users,
    long ConnectionRevision,
    DateTimeOffset? LastSynchronizedAt);

public sealed record Sub2ApiUserSnapshot(
    Guid Id,
    long ExternalId,
    string Email,
    string? Username,
    string Status,
    bool IsSelected,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RetiredAt);

public sealed record Sub2ApiUserSynchronizationResult(
    int Added,
    int Updated,
    int Retired,
    int Total,
    DateTimeOffset SynchronizedAt,
    long ConfigurationRevision);

public sealed record UpdateSub2ApiUserScopeCommand(
    Sub2ApiUserScopeMode Mode,
    IReadOnlyList<Guid> SelectedUserIds,
    long ExpectedRevision);

public sealed class Sub2ApiUserScopeException(string message) : InvalidOperationException(message);
