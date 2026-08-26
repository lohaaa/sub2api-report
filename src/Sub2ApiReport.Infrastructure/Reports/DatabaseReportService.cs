using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Reports;

internal sealed class DatabaseReportService(
    ReportDbContext dbContext,
    ISub2ApiClient sub2ApiClient,
    ISub2ApiConnectionService connectionService,
    ISystemSettingsService settingsService,
    TimeProvider timeProvider) : IReportService
{
    public async Task<ReportDocument> GenerateDryRunAsync(
        GenerateReportCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var timezone = ResolveTimezone(settings.Timezone);
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timezone).Date);
        var latestCutoffDate = localToday.AddDays(-1);
        var cutoffDate = command.CutoffDate ?? latestCutoffDate;
        if (cutoffDate > latestCutoffDate)
        {
            throw new ReportGenerationPreconditionException(
                "The report cutoff date must be earlier than the current local date.");
        }

        if (cutoffDate.DayNumber < 29)
        {
            throw new ReportGenerationPreconditionException(
                "The report cutoff date is too early to form a 30-day window.");
        }

        var credentials = await connectionService.GetCredentialsAsync(cancellationToken);
        var keys = await dbContext.ExternalApiKeys
            .AsNoTracking()
            .OrderBy(key => key.ExternalId)
            .Select(key => new KeySnapshot(
                key.Id,
                key.ExternalId,
                key.NameSnapshot,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt))
            .ToListAsync(cancellationToken);
        if (keys.Count == 0)
        {
            throw new ReportGenerationPreconditionException(
                "Synchronize the Sub2API key inventory before generating a report.");
        }

        var thirtyDayStart = cutoffDate.AddDays(-29);
        var sevenDayStart = cutoffDate.AddDays(-6);
        var assignments = await dbContext.PersonApiKeyAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.ValidFrom <= cutoffDate
                && (assignment.ValidTo == null || assignment.ValidTo >= thirtyDayStart))
            .Select(assignment => new AssignmentSnapshot(
                assignment.ExternalApiKeyId,
                assignment.PersonId,
                assignment.Person.Code,
                assignment.Person.DisplayName,
                assignment.ValidFrom,
                assignment.ValidTo))
            .ToListAsync(cancellationToken);
        var assignmentsByKey = assignments
            .GroupBy(assignment => assignment.ExternalApiKeyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AssignmentSnapshot>)group.ToArray());

        var work = keys
            .SelectMany(key => BuildSegments(
                key,
                assignmentsByKey.GetValueOrDefault(key.Id) ?? [],
                thirtyDayStart,
                sevenDayStart,
                cutoffDate))
            .ToArray();
        var results = await CollectAsync(
            work,
            credentials,
            settings.Timezone,
            settings.ReportConcurrency,
            cancellationToken);
        var generatedAt = timeProvider.GetUtcNow();
        var reportId = Guid.NewGuid();
        var document = BuildDocument(
            reportId,
            generatedAt,
            settings.Timezone,
            credentials.Revision,
            sevenDayStart,
            thirtyDayStart,
            cutoffDate,
            keys,
            results);
        var canonicalJson = ReportCanonicalSerializer.Serialize(document);
        var report = ReportSnapshot.Create(
            document.ReportId,
            document.Status,
            document.Trigger,
            cutoffDate,
            document.Timezone,
            document.ConnectionRevision,
            document.GeneratedAt,
            document.People.Count,
            document.Keys.Count,
            document.Diagnostics.FailedSegments.Count,
            document.Diagnostics.UnassignedSegments.Count,
            document.SevenDayTotal.TotalActualCost,
            document.ThirtyDayTotal.TotalActualCost,
            canonicalJson);
        dbContext.ReportSnapshots.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task<ReportPage> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "The report page is invalid.");
        }

        var total = await dbContext.ReportSnapshots.CountAsync(cancellationToken);
        var items = await dbContext.ReportSnapshots
            .AsNoTracking()
            .OrderByDescending(report => report.GeneratedAtUnixMilliseconds)
            .ThenByDescending(report => report.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(report => new ReportListItem(
                report.Id,
                report.SchemaVersion,
                report.Status,
                report.Trigger,
                report.CutoffDate,
                report.Timezone,
                report.GeneratedAt,
                report.PersonCount,
                report.KeyCount,
                report.FailedSegmentCount,
                report.UnassignedSegmentCount,
                report.SevenDayActualCost,
                report.ThirtyDayActualCost))
            .ToListAsync(cancellationToken);
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new ReportPage(items, total, page, pageSize, pages);
    }

    public async Task<ReportDocument?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var canonicalJson = await dbContext.ReportSnapshots
            .AsNoTracking()
            .Where(report => report.Id == id)
            .Select(report => report.CanonicalJson)
            .SingleOrDefaultAsync(cancellationToken);
        return canonicalJson is null ? null : ReportCanonicalSerializer.Deserialize(canonicalJson);
    }

    public async Task<ReportCsv?> GetCsvAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await GetAsync(id, cancellationToken);
        return report is null
            ? null
            : new ReportCsv(
                ReportCsvSerializer.Serialize(report),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"sub2api-report-{report.ThirtyDayWindow.EndDate:yyyy-MM-dd}.csv"));
    }

    private async Task<IReadOnlyList<SegmentResult>> CollectAsync(
        IReadOnlyList<WorkSegment> work,
        Sub2ApiConnectionCredentials credentials,
        string timezone,
        int maximumConcurrency,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = new Task<SegmentResult>[work.Count];
        for (var index = 0; index < work.Count; index++)
        {
            tasks[index] = CollectSegmentAsync(
                work[index],
                credentials,
                timezone,
                semaphore,
                cancellationToken);
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<SegmentResult> CollectSegmentAsync(
        WorkSegment segment,
        Sub2ApiConnectionCredentials credentials,
        string timezone,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var stats = await sub2ApiClient.GetUsageStatsAsync(
                credentials,
                segment.Key.ExternalId,
                segment.StartDate,
                segment.EndDate,
                timezone,
                cancellationToken);
            return new SegmentResult(segment, Map(stats), null);
        }
        catch (Sub2ApiClientException exception)
        {
            return new SegmentResult(segment, null, exception.Kind);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static ReportDocument BuildDocument(
        Guid reportId,
        DateTimeOffset generatedAt,
        string timezone,
        long connectionRevision,
        DateOnly sevenDayStart,
        DateOnly thirtyDayStart,
        DateOnly cutoffDate,
        IReadOnlyList<KeySnapshot> keys,
        IReadOnlyList<SegmentResult> results)
    {
        var sevenDayTotal = new MetricsAccumulator();
        var thirtyDayTotal = new MetricsAccumulator();
        var personUsage = new Dictionary<Guid, PersonAccumulator>();
        var failed = new List<ReportSegmentDiagnostic>();
        var unassigned = new List<ReportSegmentDiagnostic>();
        var conflicting = new List<ReportSegmentDiagnostic>();
        var hasMaterialUnassignedUsage = false;

        foreach (var result in results)
        {
            var segment = result.Segment;
            foreach (var owner in segment.Owners)
            {
                personUsage.TryAdd(
                    owner.PersonId,
                    new PersonAccumulator(owner.PersonId, owner.Code, owner.DisplayName));
            }

            if (result.FailureKind is { } failureKind)
            {
                failed.Add(CreateDiagnostic(segment, "upstream_failure", failureKind));
                if (segment.Owners.Count == 0)
                {
                    unassigned.Add(CreateDiagnostic(segment, "unassigned", null));
                }

                if (segment.Owners.Count > 1)
                {
                    conflicting.Add(CreateDiagnostic(segment, "assignment_conflict", null));
                }

                continue;
            }

            var metrics = result.Metrics!;
            thirtyDayTotal.Add(metrics);
            if (segment.StartDate >= sevenDayStart)
            {
                sevenDayTotal.Add(metrics);
            }

            if (segment.Owners.Count == 1)
            {
                var owner = segment.Owners[0];
                var accumulator = personUsage[owner.PersonId];
                accumulator.KeyIds.Add(segment.Key.Id);
                accumulator.ThirtyDay.Add(metrics);
                if (segment.StartDate >= sevenDayStart)
                {
                    accumulator.SevenDay.Add(metrics);
                }
            }
            else if (segment.Owners.Count == 0)
            {
                unassigned.Add(CreateDiagnostic(segment, "unassigned", null));
                hasMaterialUnassignedUsage |= !IsZero(metrics);
            }
            else
            {
                conflicting.Add(CreateDiagnostic(segment, "assignment_conflict", null));
            }
        }

        var keyUsage = keys.Select(key =>
        {
            var keyResults = results
                .Where(result => result.Segment.Key.Id == key.Id)
                .OrderBy(result => result.Segment.StartDate)
                .ToArray();
            var sevenDay = new MetricsAccumulator();
            var thirtyDay = new MetricsAccumulator();
            foreach (var result in keyResults.Where(result => result.Metrics is not null))
            {
                thirtyDay.Add(result.Metrics!);
                if (result.Segment.StartDate >= sevenDayStart)
                {
                    sevenDay.Add(result.Metrics!);
                }
            }

            return new ReportKeyUsage(
                key.Id,
                key.ExternalId.ToString(CultureInfo.InvariantCulture),
                key.Name,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt,
                sevenDay.ToMetrics(),
                thirtyDay.ToMetrics(),
                keyResults.Select(MapSegment).ToArray());
        }).ToArray();
        var zeroUsageKeyIds = keyUsage
            .Where(key =>
                key.Segments.All(segment => segment.FailureKind is null)
                && IsZero(key.ThirtyDay))
            .Select(key => key.ExternalId)
            .ToArray();
        var status = failed.Count > 0 || conflicting.Count > 0 || hasMaterialUnassignedUsage
            ? ReportStatus.Partial
            : ReportStatus.Complete;
        var people = personUsage.Values
            .OrderBy(person => person.Code, StringComparer.Ordinal)
            .Select(person => new ReportPersonUsage(
                person.PersonId,
                person.Code,
                person.DisplayName,
                person.KeyIds.Count,
                person.SevenDay.ToMetrics(),
                person.ThirtyDay.ToMetrics()))
            .ToArray();

        return new ReportDocument(
            ReportSnapshot.CurrentSchemaVersion,
            reportId,
            status,
            ReportTrigger.ManualDryRun,
            generatedAt,
            timezone,
            connectionRevision,
            new ReportWindow(7, sevenDayStart, cutoffDate),
            new ReportWindow(30, thirtyDayStart, cutoffDate),
            sevenDayTotal.ToMetrics(),
            thirtyDayTotal.ToMetrics(),
            people,
            keyUsage,
            new ReportDiagnostics(failed, unassigned, conflicting, zeroUsageKeyIds));
    }

    private static List<WorkSegment> BuildSegments(
        KeySnapshot key,
        IReadOnlyList<AssignmentSnapshot> assignments,
        DateOnly thirtyDayStart,
        DateOnly sevenDayStart,
        DateOnly cutoffDate)
    {
        var endExclusive = cutoffDate.AddDays(1);
        var boundaries = new SortedSet<DateOnly> { thirtyDayStart, sevenDayStart, endExclusive };
        foreach (var assignment in assignments)
        {
            if (assignment.ValidFrom > thirtyDayStart && assignment.ValidFrom <= cutoffDate)
            {
                boundaries.Add(assignment.ValidFrom);
            }

            if (assignment.ValidTo is { } validTo && validTo >= thirtyDayStart && validTo < cutoffDate)
            {
                boundaries.Add(validTo.AddDays(1));
            }
        }

        var points = boundaries.ToArray();
        var segments = new List<WorkSegment>(points.Length - 1);
        for (var index = 0; index < points.Length - 1; index++)
        {
            var startDate = points[index];
            var segmentEnd = points[index + 1].AddDays(-1);
            var owners = assignments
                .Where(assignment =>
                    assignment.ValidFrom <= startDate
                    && (assignment.ValidTo is null || assignment.ValidTo >= startDate))
                .OrderBy(assignment => assignment.Code, StringComparer.Ordinal)
                .ToArray();
            segments.Add(new WorkSegment(key, startDate, segmentEnd, owners));
        }

        return segments;
    }

    private static ReportKeySegment MapSegment(SegmentResult result)
    {
        var segment = result.Segment;
        var owner = segment.Owners.Count == 1 ? segment.Owners[0] : null;
        var code = result.FailureKind is not null
            ? "upstream_failure"
            : segment.Owners.Count == 0
                ? "unassigned"
                : segment.Owners.Count > 1
                    ? "assignment_conflict"
                    : null;
        return new ReportKeySegment(
            segment.StartDate,
            segment.EndDate,
            owner?.PersonId,
            owner?.Code,
            owner?.DisplayName,
            result.Metrics,
            result.FailureKind,
            code);
    }

    private static ReportSegmentDiagnostic CreateDiagnostic(
        WorkSegment segment,
        string code,
        Sub2ApiFailureKind? failureKind) => new(
            segment.Key.ExternalId.ToString(CultureInfo.InvariantCulture),
            segment.Key.Name,
            segment.StartDate,
            segment.EndDate,
            code,
            failureKind);

    private static ReportUsageMetrics Map(Sub2ApiUsageStats stats) => new(
        stats.TotalRequests,
        stats.TotalInputTokens,
        stats.TotalOutputTokens,
        stats.TotalCacheTokens,
        stats.TotalCacheCreationTokens,
        stats.TotalCacheReadTokens,
        stats.TotalTokens,
        stats.TotalCost,
        stats.TotalActualCost,
        stats.AverageDurationMs);

    private static bool IsZero(ReportUsageMetrics metrics) => metrics is
    {
        TotalRequests: 0,
        TotalInputTokens: 0,
        TotalOutputTokens: 0,
        TotalCacheTokens: 0,
        TotalTokens: 0,
        TotalCost: 0,
        TotalActualCost: 0,
    };

    private static TimeZoneInfo ResolveTimezone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ReportGenerationPreconditionException("The configured report time zone is invalid.");
        }
    }

    private sealed record KeySnapshot(
        Guid Id,
        long ExternalId,
        string Name,
        string Status,
        DateTimeOffset? LastUsedAt,
        DateTimeOffset? RetiredAt);

    private sealed record AssignmentSnapshot(
        Guid ExternalApiKeyId,
        Guid PersonId,
        string Code,
        string DisplayName,
        DateOnly ValidFrom,
        DateOnly? ValidTo);

    private sealed record WorkSegment(
        KeySnapshot Key,
        DateOnly StartDate,
        DateOnly EndDate,
        IReadOnlyList<AssignmentSnapshot> Owners);

    private sealed record SegmentResult(
        WorkSegment Segment,
        ReportUsageMetrics? Metrics,
        Sub2ApiFailureKind? FailureKind);

    private sealed class PersonAccumulator(
        Guid personId,
        string code,
        string displayName)
    {
        public Guid PersonId { get; } = personId;

        public string Code { get; } = code;

        public string DisplayName { get; } = displayName;

        public HashSet<Guid> KeyIds { get; } = [];

        public MetricsAccumulator SevenDay { get; } = new();

        public MetricsAccumulator ThirtyDay { get; } = new();
    }

    private sealed class MetricsAccumulator
    {
        private long _totalRequests;
        private long _totalInputTokens;
        private long _totalOutputTokens;
        private long _totalCacheTokens;
        private long _totalCacheCreationTokens;
        private long _totalCacheReadTokens;
        private long _totalTokens;
        private decimal _totalCost;
        private decimal _totalActualCost;
        private decimal _weightedDuration;

        public void Add(ReportUsageMetrics metrics)
        {
            checked
            {
                _totalRequests += metrics.TotalRequests;
                _totalInputTokens += metrics.TotalInputTokens;
                _totalOutputTokens += metrics.TotalOutputTokens;
                _totalCacheTokens += metrics.TotalCacheTokens;
                _totalCacheCreationTokens += metrics.TotalCacheCreationTokens;
                _totalCacheReadTokens += metrics.TotalCacheReadTokens;
                _totalTokens += metrics.TotalTokens;
                _totalCost += metrics.TotalCost;
                _totalActualCost += metrics.TotalActualCost;
                _weightedDuration += metrics.AverageDurationMs * metrics.TotalRequests;
            }
        }

        public ReportUsageMetrics ToMetrics() => new(
            _totalRequests,
            _totalInputTokens,
            _totalOutputTokens,
            _totalCacheTokens,
            _totalCacheCreationTokens,
            _totalCacheReadTokens,
            _totalTokens,
            _totalCost,
            _totalActualCost,
            _totalRequests == 0 ? 0 : _weightedDuration / _totalRequests);
    }
}
