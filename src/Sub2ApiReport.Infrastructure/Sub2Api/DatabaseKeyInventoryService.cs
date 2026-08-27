using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Sub2Api;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Sub2Api;

internal sealed class DatabaseKeyInventoryService(
    ReportDbContext dbContext,
    ISub2ApiConnectionService connectionService,
    ISub2ApiClient client,
    TimeProvider timeProvider) : IKeyInventoryService
{
    private static readonly SemaphoreSlim SynchronizationLock = new(1, 1);

    public async Task<KeySynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken)
    {
        await SynchronizationLock.WaitAsync(cancellationToken);
        try
        {
            var credentials = await connectionService.GetCredentialsAsync(cancellationToken);
            var connection = await dbContext.Sub2ApiConnections.AsNoTracking().SingleAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
            var usersQuery = dbContext.Sub2ApiUsers.AsNoTracking()
                .Where(user => user.RetiredAt == null && user.Status == "active");
            if (connection.UserScopeMode == Sub2ApiUserScopeMode.SelectedUsers)
            {
                usersQuery = usersQuery.Where(user => user.IsSelected);
            }

            var targetUsers = await usersQuery.OrderBy(user => user.ExternalId).ToListAsync(cancellationToken);
            if (targetUsers.Count == 0)
            {
                throw new Sub2ApiUserScopeException("请先同步并选择至少一个 Sub2API 用户。");
            }

            var remote = new List<(Sub2ApiUser User, Sub2ApiExternalKey Key)>();
            foreach (var user in targetUsers)
            {
                var keys = await client.GetApiKeysAsync(credentials, user.ExternalId, cancellationToken);
                remote.AddRange(keys.Select(key => (user, key)));
            }

            var now = timeProvider.GetUtcNow();
            var existingKeys = await dbContext.ExternalApiKeys.ToListAsync(cancellationToken);
            var existingByIdentity = existingKeys
                .Where(key => key.Sub2ApiUserId.HasValue)
                .ToDictionary(key => (key.Sub2ApiUserId!.Value, key.ExternalId));
            var legacyByExternalId = existingKeys
                .Where(key => !key.Sub2ApiUserId.HasValue)
                .ToDictionary(key => key.ExternalId);
            var seen = new HashSet<(Guid UserId, long KeyId)>();
            var added = 0;
            var updated = 0;

            foreach (var (user, remoteKey) in remote)
            {
                var identity = (user.Id, remoteKey.ExternalId);
                seen.Add(identity);
                if (existingByIdentity.TryGetValue(identity, out var existing))
                {
                    if (existing.ApplySnapshot(
                        remoteKey.Name,
                        remoteKey.Status,
                        remoteKey.GroupId,
                        remoteKey.LastUsedAt,
                        now))
                    {
                        updated++;
                    }
                }
                else if (connection.LegacyUserId == user.ExternalId
                    && legacyByExternalId.TryGetValue(remoteKey.ExternalId, out var legacy))
                {
                    legacy.AssignUser(user.Id);
                    legacy.ApplySnapshot(
                        remoteKey.Name,
                        remoteKey.Status,
                        remoteKey.GroupId,
                        remoteKey.LastUsedAt,
                        now);
                    updated++;
                }
                else
                {
                    dbContext.ExternalApiKeys.Add(ExternalApiKey.Create(
                        user.Id,
                        remoteKey.ExternalId,
                        remoteKey.Name,
                        remoteKey.Status,
                        remoteKey.GroupId,
                        remoteKey.LastUsedAt,
                        now));
                    added++;
                }
            }

            var targetIds = targetUsers.Select(user => user.Id).ToHashSet();
            var retired = existingKeys.Count(key =>
                key.Sub2ApiUserId.HasValue
                && targetIds.Contains(key.Sub2ApiUserId.Value)
                && !seen.Contains((key.Sub2ApiUserId.Value, key.ExternalId))
                && key.MarkRetired(now));
            var trackedConnection = await dbContext.Sub2ApiConnections.SingleAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
            if (trackedConnection.Revision != credentials.Revision)
            {
                throw new Sub2ApiConnectionConflictException(
                    credentials.Revision,
                    trackedConnection.Revision);
            }

            trackedConnection.RecordSynchronization(remote.Count, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new KeySynchronizationResult(
                added,
                updated,
                retired,
                remote.Count,
                now,
                credentials.Revision);
        }
        finally
        {
            SynchronizationLock.Release();
        }
    }

    public async Task<ApiKeyInventoryPage> GetPageAsync(
        ApiKeyInventoryQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page must be positive.");
        }

        if (query.PageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page size must be between 1 and 100.");
        }

        IQueryable<ExternalApiKey> keysQuery = dbContext.ExternalApiKeys
            .AsNoTracking()
            .Include(key => key.Sub2ApiUser);
        if (query.RetiredOnly)
        {
            keysQuery = keysQuery.Where(key => key.RetiredAt != null);
        }

        var total = await keysQuery.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
        var keys = await keysQuery
            .OrderBy(key => key.RetiredAt != null)
            .ThenBy(key => key.ExternalId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var retiredKeys = await dbContext.ExternalApiKeys
            .AsNoTracking()
            .CountAsync(key => key.RetiredAt != null, cancellationToken);
        var connection = await dbContext.Sub2ApiConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
        return new ApiKeyInventoryPage(
            keys.Select(key => new ApiKeyInventoryItem(
                key.Id,
                key.ExternalId,
                key.Sub2ApiUser?.ExternalId,
                key.Sub2ApiUser?.EmailSnapshot,
                key.NameSnapshot,
                key.Status,
                key.GroupId,
                key.LastUsedAt,
                key.LastSeenAt,
                key.RetiredAt))
                .ToArray(),
            total,
            query.Page,
            query.PageSize,
            pages,
            new ApiKeyInventoryDiagnostics(retiredKeys),
            connection?.LastSynchronizedAt);
    }
}
