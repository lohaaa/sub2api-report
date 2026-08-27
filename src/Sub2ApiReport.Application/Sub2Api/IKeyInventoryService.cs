namespace Sub2ApiReport.Application.Sub2Api;

public interface IKeyInventoryService
{
    Task<KeySynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken);

    Task<ApiKeyInventoryPage> GetPageAsync(
        ApiKeyInventoryQuery query,
        CancellationToken cancellationToken);
}

public sealed record ApiKeyInventoryQuery(
    int Page,
    int PageSize,
    bool RetiredOnly);

public sealed record KeySynchronizationResult(
    int Added,
    int Updated,
    int Retired,
    int Total,
    DateTimeOffset SynchronizedAt,
    long ConfigurationRevision);

public sealed record ApiKeyInventoryPage(
    IReadOnlyList<ApiKeyInventoryItem> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages,
    ApiKeyInventoryDiagnostics Diagnostics,
    DateTimeOffset? LastSynchronizedAt);

public sealed record ApiKeyInventoryItem(
    Guid Id,
    long ExternalId,
    long? SourceUserId,
    string? SourceUserEmail,
    string Name,
    string Status,
    long? GroupId,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RetiredAt);

public sealed record ApiKeyInventoryDiagnostics(
    int RetiredKeys);
