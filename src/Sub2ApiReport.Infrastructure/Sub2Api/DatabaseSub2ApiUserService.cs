using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Sub2Api;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Sub2Api;

internal sealed class DatabaseSub2ApiUserService(
    ReportDbContext dbContext,
    ISub2ApiConnectionService connectionService,
    ISub2ApiClient client,
    TimeProvider timeProvider) : ISub2ApiUserService
{
    private static readonly SemaphoreSlim SynchronizationLock = new(1, 1);

    public async Task<Sub2ApiUserScopeSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var connection = await dbContext.Sub2ApiConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == Sub2ApiConnection.SingletonId, cancellationToken);
        var users = await dbContext.Sub2ApiUsers.AsNoTracking()
            .OrderBy(user => user.EmailSnapshot)
            .ThenBy(user => user.ExternalId)
            .ToListAsync(cancellationToken);
        return new Sub2ApiUserScopeSnapshot(
            connection?.UserScopeMode ?? Sub2ApiUserScopeMode.SelectedUsers,
            users.Select(Map).ToArray(),
            connection?.Revision ?? 0,
            connection?.LastUsersSynchronizedAt);
    }

    public async Task<Sub2ApiUserSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken)
    {
        await SynchronizationLock.WaitAsync(cancellationToken);
        try
        {
            var credentials = await connectionService.GetCredentialsAsync(cancellationToken);
            var remoteUsers = await client.GetUsersAsync(credentials, cancellationToken);
            var connection = await dbContext.Sub2ApiConnections.SingleAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
            var now = timeProvider.GetUtcNow();
            var existing = await dbContext.Sub2ApiUsers.ToListAsync(cancellationToken);
            var byExternalId = existing.ToDictionary(user => user.ExternalId);
            var seen = new HashSet<long>();
            var added = 0;
            var updated = 0;

            foreach (var remote in remoteUsers)
            {
                seen.Add(remote.ExternalId);
                if (byExternalId.TryGetValue(remote.ExternalId, out var local))
                {
                    if (connection.LegacyUserId == remote.ExternalId)
                    {
                        local.SetSelected(true);
                    }

                    if (local.ApplySnapshot(remote.Email, remote.Username, remote.Status, now))
                    {
                        updated++;
                    }
                }
                else
                {
                    dbContext.Sub2ApiUsers.Add(Sub2ApiUser.Create(
                        remote.ExternalId,
                        remote.Email,
                        remote.Username,
                        remote.Status,
                        isSelected: connection.LegacyUserId == remote.ExternalId,
                        now));
                    added++;
                }
            }

            var retired = existing.Count(user => !seen.Contains(user.ExternalId) && user.MarkRetired(now));
            if (connection.Revision != credentials.Revision)
            {
                throw new Sub2ApiConnectionConflictException(credentials.Revision, connection.Revision);
            }

            connection.RecordUserSynchronization(remoteUsers.Count, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new Sub2ApiUserSynchronizationResult(
                added,
                updated,
                retired,
                remoteUsers.Count,
                now,
                connection.Revision);
        }
        finally
        {
            SynchronizationLock.Release();
        }
    }

    public async Task<Sub2ApiUserScopeSnapshot> UpdateScopeAsync(
        UpdateSub2ApiUserScopeCommand command,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Sub2ApiConnections.SingleOrDefaultAsync(
            item => item.Id == Sub2ApiConnection.SingletonId,
            cancellationToken) ?? throw new Sub2ApiConnectionNotConfiguredException();
        if (connection.Revision != command.ExpectedRevision)
        {
            throw new Sub2ApiConnectionConflictException(command.ExpectedRevision, connection.Revision);
        }

        var selected = command.SelectedUserIds.Distinct().ToHashSet();
        var users = await dbContext.Sub2ApiUsers.ToListAsync(cancellationToken);
        if (command.Mode == Sub2ApiUserScopeMode.SelectedUsers)
        {
            if (selected.Count == 0)
            {
                throw new Sub2ApiUserScopeException("至少选择一个 Sub2API 用户。");
            }

            if (users.Count(user => selected.Contains(user.Id) && user.RetiredAt is null) != selected.Count)
            {
                throw new Sub2ApiUserScopeException("选择中包含不存在或已退休的 Sub2API 用户。");
            }
        }

        foreach (var user in users)
        {
            user.SetSelected(command.Mode == Sub2ApiUserScopeMode.SelectedUsers && selected.Contains(user.Id));
        }

        connection.UpdateUserScope(command.Mode, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static Sub2ApiUserSnapshot Map(Sub2ApiUser user) => new(
        user.Id,
        user.ExternalId,
        user.EmailSnapshot,
        user.UsernameSnapshot,
        user.Status,
        user.IsSelected,
        user.LastSeenAt,
        user.RetiredAt);
}
