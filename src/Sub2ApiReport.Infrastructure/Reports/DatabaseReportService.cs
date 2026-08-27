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
    ISub2ApiUserService userService,
    IKeyInventoryService keyInventoryService,
    ISystemSettingsService settingsService,
    TimeProvider timeProvider) : IReportService
{
    public async Task<ReportDocument> GenerateDryRunAsync(
        GenerateReportCommand command,
        CancellationToken cancellationToken)
    {
        var run = dbContext.ReportGenerationRuns.Add(ReportGenerationRun.Start(
            ReportTrigger.ManualDryRun,
            0,
            timeProvider.GetUtcNow())).Entity;
        await dbContext.SaveChangesAsync(cancellationToken);
        var stage = "prepare";
        try
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
            run.MarkConnectionRevision(credentials.Revision);
            await dbContext.SaveChangesAsync(cancellationToken);

            stage = "user_sync";
            await userService.SynchronizeAsync(cancellationToken);

            stage = "key_sync";
            await keyInventoryService.SynchronizeAsync(cancellationToken);

            stage = "collect";
            var keys = await LoadKeySnapshotsAsync(cutoffDate, cancellationToken);
            if (keys.Count == 0)
            {
                throw new ReportGenerationPreconditionException(
                    "所选 Sub2API 用户暂无 API Key，请检查统计用户范围。");
            }

            var thirtyDayStart = cutoffDate.AddDays(-29);
            var sevenDayStart = cutoffDate.AddDays(-6);
            var results = await CollectAsync(
                keys,
                credentials,
                settings.Timezone,
                settings.ReportConcurrency,
                sevenDayStart,
                thirtyDayStart,
                cutoffDate,
                cancellationToken);

            stage = "snapshot";
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
                document.Users.Count,
                document.Keys.Count,
                document.Diagnostics.FailedRanges.Count,
                document.SevenDayTotal.TotalActualCost,
                document.ThirtyDayTotal.TotalActualCost,
                canonicalJson);
            dbContext.ReportSnapshots.Add(report);
            run.MarkSucceeded(report.Id, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return document;
        }
        catch (Exception exception) when (exception is
            Sub2ApiClientException or
            Sub2ApiUserScopeException or
            Sub2ApiConnectionNotConfiguredException or
            Sub2ApiConnectionConflictException or
            ReportGenerationPreconditionException)
        {
            run.MarkFailed(
                stage,
                DescribeErrorCode(exception),
                DescribeErrorMessage(exception),
                run.ConnectionRevision,
                timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
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
                report.UserCount,
                report.KeyCount,
                report.FailedRangeCount,
                report.SevenDayActualCost,
                report.ThirtyDayActualCost))
            .ToListAsync(cancellationToken);
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new ReportPage(items, total, page, pageSize, pages);
    }

    public async Task<ReportGenerationRunPage> GetGenerationRunsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "The generation run page is invalid.");
        }

        var total = await dbContext.ReportGenerationRuns.CountAsync(cancellationToken);
        var items = await dbContext.ReportGenerationRuns
            .AsNoTracking()
            .OrderByDescending(item => item.StartedAtUnixMilliseconds)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new ReportGenerationRunItem(
                item.Id,
                item.Trigger,
                item.Status,
                item.Stage,
                item.ErrorCode,
                item.ErrorMessage,
                item.ConnectionRevision,
                item.StartedAt,
                item.CompletedAt,
                item.ReportSnapshotId))
            .ToListAsync(cancellationToken);
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new ReportGenerationRunPage(items, total, page, pageSize, pages);
    }

    public async Task<ReportDocument?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.ReportSnapshots
            .AsNoTracking()
            .Where(report => report.Id == id)
            .Select(report => new { report.SchemaVersion, report.CanonicalJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return snapshot.SchemaVersion >= ReportSnapshot.CurrentSchemaVersion
            ? ReportCanonicalSerializer.Deserialize(snapshot.CanonicalJson)
            : LegacyReportDocumentMapper.MapFromLegacy(
                ReportCanonicalSerializer.DeserializeLegacy(snapshot.CanonicalJson));
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

    private async Task<IReadOnlyList<KeySnapshot>> LoadKeySnapshotsAsync(
        DateOnly cutoffDate,
        CancellationToken cancellationToken)
    {
        var scopeMode = await dbContext.Sub2ApiConnections.AsNoTracking()
            .Where(item => item.Id == Domain.Sub2Api.Sub2ApiConnection.SingletonId)
            .Select(item => item.UserScopeMode)
            .SingleAsync(cancellationToken);
        var keys = await dbContext.ExternalApiKeys
            .AsNoTracking()
            .Where(key => key.Sub2ApiUser != null
                && key.Sub2ApiUser.RetiredAt == null
                && key.Sub2ApiUser.Status == "active"
                && (scopeMode == Domain.Sub2Api.Sub2ApiUserScopeMode.AllActiveUsers
                    || key.Sub2ApiUser.IsSelected))
            .OrderBy(key => key.Sub2ApiUser!.ExternalId)
            .ThenBy(key => key.ExternalId)
            .Select(key => new KeySnapshot(
                key.Id,
                key.Sub2ApiUser!.Id,
                key.Sub2ApiUser.ExternalId,
                key.Sub2ApiUser.EmailSnapshot,
                key.Sub2ApiUser.UsernameSnapshot,
                key.ExternalId,
                key.NameSnapshot,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt))
            .ToListAsync(cancellationToken);
        return keys;
    }

    private async Task<IReadOnlyList<WindowResult>> CollectAsync(
        IReadOnlyList<KeySnapshot> keys,
        Sub2ApiConnectionCredentials credentials,
        string timezone,
        int maximumConcurrency,
        DateOnly sevenDayStart,
        DateOnly thirtyDayStart,
        DateOnly cutoffDate,
        CancellationToken cancellationToken)
    {
        var work = keys
            .SelectMany(key => new WorkWindow[]
            {
                new(key, sevenDayStart, cutoffDate, true),
                new(key, thirtyDayStart, cutoffDate, false),
            })
            .ToArray();
        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = new Task<WindowResult>[work.Length];
        for (var index = 0; index < work.Length; index++)
        {
            tasks[index] = CollectWindowAsync(
                work[index],
                credentials,
                timezone,
                semaphore,
                cancellationToken);
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<WindowResult> CollectWindowAsync(
        WorkWindow window,
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
                window.Key.ExternalUserId,
                window.Key.ExternalId,
                window.StartDate,
                window.EndDate,
                timezone,
                cancellationToken);
            return new WindowResult(window, Map(stats), null);
        }
        catch (Sub2ApiClientException exception)
        {
            return new WindowResult(window, null, exception);
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
        IReadOnlyList<WindowResult> results)
    {
        var resultsByKey = results
            .GroupBy(result => result.Window.Key.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var userAccumulators = new Dictionary<Guid, UserAccumulator>();
        var keyUsage = keys.Select(key =>
        {
            var keyResults = resultsByKey.GetValueOrDefault(key.Id) ?? [];
            var sevenDay = new MetricsAccumulator();
            var thirtyDay = new MetricsAccumulator();
            foreach (var result in keyResults)
            {
                if (result.Metrics is null)
                {
                    continue;
                }

                if (result.Window.IsSevenDay)
                {
                    sevenDay.Add(result.Metrics);
                }
                else
                {
                    thirtyDay.Add(result.Metrics);
                }
            }

            var accumulator = userAccumulators.TryGetValue(key.UserId, out var existing)
                ? existing
                : userAccumulators[key.UserId] = new UserAccumulator(
                    key.UserId,
                    key.ExternalUserId,
                    key.UserEmail,
                    key.Username);
            accumulator.SevenDay.Add(sevenDay.ToMetrics(), 1);
            accumulator.ThirtyDay.Add(thirtyDay.ToMetrics(), 1);
            return new ReportKeyUsage(
                key.Id,
                key.ExternalId.ToString(CultureInfo.InvariantCulture),
                key.ExternalUserId,
                key.UserEmail,
                key.Name,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt,
                sevenDay.ToMetrics(),
                thirtyDay.ToMetrics());
        }).ToArray();
        var failedRanges = results
            .Where(result => result.Exception is not null)
            .Select(result => new ReportRangeFailure(
                result.Window.Key.ExternalUserId,
                result.Window.Key.UserEmail,
                result.Window.Key.ExternalId,
                result.Window.Key.Name,
                result.Window.StartDate,
                result.Window.EndDate,
                result.Exception!.Kind,
                DescribeFailureCode(result.Exception.Kind)))
            .OrderBy(item => item.ExternalUserId)
            .ThenBy(item => item.ExternalKeyId)
            .ThenBy(item => item.StartDate)
            .ToArray();
        var users = userAccumulators.Values
            .OrderBy(user => user.ExternalUserId)
            .Select(user => new ReportUserUsage(
                user.UserId,
                user.ExternalUserId,
                user.Username,
                user.Email,
                user.SevenDay.KeyCount,
                user.SevenDay.ToMetrics(),
                user.ThirtyDay.ToMetrics()))
            .ToArray();
        var sevenDayTotal = new MetricsAccumulator();
        var thirtyDayTotal = new MetricsAccumulator();
        foreach (var user in users)
        {
            sevenDayTotal.Add(user.SevenDay, 0);
            thirtyDayTotal.Add(user.ThirtyDay, 0);
        }

        return new ReportDocument(
            ReportSnapshot.CurrentSchemaVersion,
            reportId,
            failedRanges.Length == 0 ? ReportStatus.Complete : ReportStatus.Partial,
            ReportTrigger.ManualDryRun,
            generatedAt,
            timezone,
            connectionRevision,
            new ReportWindow(7, sevenDayStart, cutoffDate),
            new ReportWindow(30, thirtyDayStart, cutoffDate),
            sevenDayTotal.ToMetrics(),
            thirtyDayTotal.ToMetrics(),
            users,
            keyUsage,
            new ReportDiagnostics(failedRanges));
    }

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

    private static string DescribeFailureCode(Sub2ApiFailureKind kind) => kind switch
    {
        Sub2ApiFailureKind.Unauthorized => "unauthorized",
        Sub2ApiFailureKind.Forbidden => "forbidden",
        Sub2ApiFailureKind.Incompatible => "incompatible",
        Sub2ApiFailureKind.RateLimited => "rate-limited",
        Sub2ApiFailureKind.Timeout => "timeout",
        Sub2ApiFailureKind.Unavailable => "unavailable",
        _ => "invalid-response",
    };

    private static string DescribeErrorCode(Exception exception) => exception switch
    {
        Sub2ApiClientException client => DescribeFailureCode(client.Kind),
        Sub2ApiUserScopeException => "user-scope",
        Sub2ApiConnectionNotConfiguredException => "connection-not-configured",
        Sub2ApiConnectionConflictException => "connection-changed",
        _ => "precondition",
    };

    private static string DescribeErrorMessage(Exception exception) => exception switch
    {
        Sub2ApiClientException client => client.Kind switch
        {
            Sub2ApiFailureKind.Unauthorized => "Admin API Key 无效，无法刷新 Sub2API 数据。",
            Sub2ApiFailureKind.Forbidden => "Admin API Key 没有读取目标用户的权限。",
            Sub2ApiFailureKind.Incompatible => "当前 Sub2API 部署不支持所需的同步接口。",
            Sub2ApiFailureKind.RateLimited => "Sub2API 暂时限流，请稍后重试。",
            Sub2ApiFailureKind.Timeout => "连接 Sub2API 超时，报告未生成。",
            Sub2ApiFailureKind.Unavailable => "Sub2API 当前不可用，报告未生成。",
            _ => "Sub2API 返回了无法识别的数据，报告未生成。",
        },
        Sub2ApiUserScopeException or
        Sub2ApiConnectionNotConfiguredException or
        Sub2ApiConnectionConflictException or
        ReportGenerationPreconditionException => exception.Message,
        _ => exception.Message,
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
        Guid UserId,
        long ExternalUserId,
        string UserEmail,
        string? Username,
        long ExternalId,
        string Name,
        string Status,
        DateTimeOffset? LastUsedAt,
        DateTimeOffset? RetiredAt);

    private sealed record WorkWindow(
        KeySnapshot Key,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsSevenDay);

    private sealed record WindowResult(
        WorkWindow Window,
        ReportUsageMetrics? Metrics,
        Sub2ApiClientException? Exception);

    private sealed class UserAccumulator(
        Guid userId,
        long externalUserId,
        string email,
        string? username)
    {
        public Guid UserId { get; } = userId;

        public long ExternalUserId { get; } = externalUserId;

        public string Email { get; } = email;

        public string? Username { get; } = username;

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
        private int _keyCount;

        public int KeyCount => _keyCount;

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

        public void Add(ReportUsageMetrics metrics, int keyCount)
        {
            _keyCount += keyCount;
            Add(metrics);
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
