using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.People;
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
            var connection = await connectionService.GetCredentialsAsync(cancellationToken);
            var remoteKeys = await client.GetApiKeysAsync(connection, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var existingKeys = await dbContext.ExternalApiKeys.ToListAsync(cancellationToken);
            var existingByExternalId = existingKeys.ToDictionary(key => key.ExternalId);
            var seenIds = new HashSet<long>();
            var added = 0;
            var updated = 0;

            foreach (var remoteKey in remoteKeys)
            {
                seenIds.Add(remoteKey.ExternalId);
                if (existingByExternalId.TryGetValue(remoteKey.ExternalId, out var existing))
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
                else
                {
                    dbContext.ExternalApiKeys.Add(ExternalApiKey.Create(
                        remoteKey.ExternalId,
                        remoteKey.Name,
                        remoteKey.Status,
                        remoteKey.GroupId,
                        remoteKey.LastUsedAt,
                        now));
                    added++;
                }
            }

            var retired = existingKeys.Count(key =>
                !seenIds.Contains(key.ExternalId) && key.MarkRetired(now));
            var trackedConnection = await dbContext.Sub2ApiConnections.SingleAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
            if (trackedConnection.Revision != connection.Revision)
            {
                throw new Sub2ApiConnectionConflictException(
                    connection.Revision,
                    trackedConnection.Revision);
            }

            trackedConnection.RecordSynchronization(remoteKeys.Count, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new KeySynchronizationResult(
                added,
                updated,
                retired,
                remoteKeys.Count,
                now,
                connection.Revision);
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

        var currentDate = await GetCurrentDateAsync(cancellationToken);
        var keysQuery = dbContext.ExternalApiKeys.AsNoTracking();
        if (query.UnmappedOnly)
        {
            keysQuery = keysQuery.Where(key =>
                !dbContext.PersonApiKeyAssignments.Any(assignment =>
                    assignment.ExternalApiKeyId == key.Id)
                || (key.RetiredAt == null
                    && key.Status == "active"
                    && !dbContext.PersonApiKeyAssignments.Any(assignment =>
                        assignment.ExternalApiKeyId == key.Id
                        && assignment.Person.IsActive
                        && assignment.ValidFrom <= currentDate
                        && (assignment.ValidTo == null || assignment.ValidTo >= currentDate))));
        }

        var total = await keysQuery.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
        var keys = await keysQuery
            .OrderBy(key => key.RetiredAt != null)
            .ThenBy(key => key.ExternalId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var keyIds = keys.Select(key => key.Id).ToArray();
        var assignments = keyIds.Length == 0
            ? []
            : await dbContext.PersonApiKeyAssignments
                .AsNoTracking()
                .Where(assignment => keyIds.Contains(assignment.ExternalApiKeyId))
                .OrderBy(assignment => assignment.ValidFrom)
                .Select(assignment => new AssignmentProjection(
                    assignment.ExternalApiKeyId,
                    assignment.Id,
                    assignment.PersonId,
                    assignment.Person.Code,
                    assignment.Person.DisplayName,
                    assignment.ValidFrom,
                    assignment.ValidTo,
                    assignment.Revision))
                .ToListAsync(cancellationToken);
        var assignmentsByKey = assignments
            .GroupBy(assignment => assignment.ExternalApiKeyId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiKeyAssignmentSnapshot>)group
                    .Select(MapAssignment)
                    .ToArray());

        var diagnostics = await GetDiagnosticsAsync(currentDate, cancellationToken);
        var connection = await dbContext.Sub2ApiConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
        return new ApiKeyInventoryPage(
            keys.Select(key => new ApiKeyInventoryItem(
                key.Id,
                key.ExternalId,
                key.NameSnapshot,
                key.Status,
                key.GroupId,
                key.LastUsedAt,
                key.LastSeenAt,
                key.RetiredAt,
                assignmentsByKey.GetValueOrDefault(key.Id, [])))
                .ToArray(),
            total,
            query.Page,
            query.PageSize,
            pages,
            diagnostics,
            connection?.LastSynchronizedAt);
    }

    private async Task<ApiKeyInventoryDiagnostics> GetDiagnosticsAsync(
        DateOnly currentDate,
        CancellationToken cancellationToken)
    {
        var unmapped = await dbContext.ExternalApiKeys
            .AsNoTracking()
            .CountAsync(key =>
                !dbContext.PersonApiKeyAssignments.Any(assignment =>
                    assignment.ExternalApiKeyId == key.Id)
                || (key.RetiredAt == null
                    && key.Status == "active"
                    && !dbContext.PersonApiKeyAssignments.Any(assignment =>
                        assignment.ExternalApiKeyId == key.Id
                        && assignment.Person.IsActive
                        && assignment.ValidFrom <= currentDate
                        && (assignment.ValidTo == null || assignment.ValidTo >= currentDate))),
                cancellationToken);
        var retired = await dbContext.ExternalApiKeys
            .AsNoTracking()
            .CountAsync(key => key.RetiredAt != null, cancellationToken);
        var allAssignments = await dbContext.PersonApiKeyAssignments
            .AsNoTracking()
            .OrderBy(assignment => assignment.ExternalApiKeyId)
            .ThenBy(assignment => assignment.ValidFrom)
            .Select(assignment => new AssignmentRange(
                assignment.ExternalApiKeyId,
                assignment.ValidFrom,
                assignment.ValidTo))
            .ToListAsync(cancellationToken);
        var overlapping = allAssignments
            .GroupBy(assignment => assignment.ExternalApiKeyId)
            .Count(group => ContainsOverlap(group.ToArray()));
        return new ApiKeyInventoryDiagnostics(unmapped, overlapping, retired);
    }

    private async Task<DateOnly> GetCurrentDateAsync(CancellationToken cancellationToken)
    {
        var timezone = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Id == Domain.System.SystemSetting.SingletonId)
            .Select(setting => setting.Timezone)
            .SingleAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timezone));
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static bool ContainsOverlap(IReadOnlyList<AssignmentRange> assignments)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            var currentEnd = assignments[index].ValidTo ?? DateOnly.MaxValue;
            for (var next = index + 1; next < assignments.Count; next++)
            {
                if (assignments[next].ValidFrom <= currentEnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ApiKeyAssignmentSnapshot MapAssignment(AssignmentProjection assignment) => new(
        assignment.Id,
        assignment.PersonId,
        assignment.PersonCode,
        assignment.PersonDisplayName,
        assignment.ValidFrom,
        assignment.ValidTo,
        assignment.Revision);

    private sealed record AssignmentProjection(
        Guid ExternalApiKeyId,
        Guid Id,
        Guid PersonId,
        string PersonCode,
        string PersonDisplayName,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        long Revision);

    private sealed record AssignmentRange(
        Guid ExternalApiKeyId,
        DateOnly ValidFrom,
        DateOnly? ValidTo);
}
