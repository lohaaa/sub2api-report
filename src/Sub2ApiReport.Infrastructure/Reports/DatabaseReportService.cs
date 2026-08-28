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

        var windows = ReportWindows.Resolve(command.Windows ?? ReportWindows.Default, cutoffDate, true);
        return await GenerateAsync(
            settings,
            cutoffDate,
            settings.Timezone,
            windows,
            ReportTrigger.ManualDryRun,
            null,
            cancellationToken);
    }

    public Task<ReportDocument> GenerateTaskReportAsync(
        GenerateTaskReportCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Trigger is ReportTrigger.ManualDryRun || command.ReportRunId == Guid.Empty)
        {
            throw new ArgumentException("The task report trigger is invalid.", nameof(command));
        }

        var timezone = ResolveTimezone(command.Timezone);
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timezone).Date);
        var latestCutoffDate = localToday.AddDays(-1);
        if (command.CutoffDate > latestCutoffDate)
        {
            throw new ReportGenerationPreconditionException(
                "The report cutoff date must be earlier than the current local date.");
        }

        var windows = ValidateFrozenWindows(command.Windows);
        return GenerateAsync(
            settings: null,
            command.CutoffDate,
            command.Timezone,
            windows,
            command.Trigger,
            command.ReportRunId,
            cancellationToken);
    }

    private async Task<ReportDocument> GenerateAsync(
        SystemSettingsSnapshot? settings,
        DateOnly cutoffDate,
        string timezoneName,
        IReadOnlyList<ResolvedReportWindow> windows,
        ReportTrigger trigger,
        Guid? reportRunId,
        CancellationToken cancellationToken)
    {
        settings ??= await settingsService.GetAsync(cancellationToken);
        var run = dbContext.ReportGenerationRuns.Add(ReportGenerationRun.Start(
            trigger,
            0,
            timeProvider.GetUtcNow(),
            reportRunId)).Entity;
        await dbContext.SaveChangesAsync(cancellationToken);
        var stage = "prepare";
        try
        {
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

            var results = await CollectAsync(
                keys,
                credentials,
                timezoneName,
                settings.ReportConcurrency,
                windows,
                cancellationToken);

            stage = "snapshot";
            ReportRun? taskRun = null;
            if (reportRunId is not null)
            {
                taskRun = await dbContext.ReportRuns.FindAsync([reportRunId.Value], cancellationToken)
                    ?? throw new InvalidOperationException("The report task run no longer exists.");
                taskRun.BeginRendering(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var generatedAt = timeProvider.GetUtcNow();
            var reportId = Guid.NewGuid();
            var document = BuildDocument(
                reportId,
                generatedAt,
                timezoneName,
                credentials.Revision,
                trigger,
                cutoffDate,
                windows,
                keys,
                results);
            var canonicalJson = ReportCanonicalSerializer.Serialize(document);
            var windowSummaries = document.Windows
                .Select(window => new ReportWindowSummary(
                    window.Key,
                    window.Label,
                    window.StartDate,
                    window.EndDateExclusive,
                    window.DayCount,
                    document.WindowTotals
                        .FirstOrDefault(total => total.WindowKey == window.Key)
                        ?.Metrics.TotalActualCost ?? 0m))
                .ToArray();
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
                document.SevenDayActualCost,
                document.ThirtyDayActualCost,
                ReportWindowSummaryJson.Serialize(windowSummaries),
                canonicalJson);
            dbContext.ReportSnapshots.Add(report);
            taskRun?.AttachSnapshot(report.Id);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.MarkFailed(
                stage,
                "cancelled",
                "报告生成已中断。",
                run.ConnectionRevision,
                timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            run.MarkFailed(
                stage,
                "internal_error",
                "报告生成因内部错误终止。",
                run.ConnectionRevision,
                timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
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
            .OrderByDescending(report => report.GeneratedAt)
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
                report.ThirtyDayActualCost,
                report.WindowSummaryJson))
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
            .OrderByDescending(item => item.StartedAt)
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

        return ReportCanonicalSerializer.Deserialize(snapshot.CanonicalJson, snapshot.SchemaVersion);
    }

    public async Task<ReportCsv?> GetCsvAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await GetAsync(id, cancellationToken);
        return report is null
            ? null
            : new ReportCsv(
                ReportCsvSerializer.Serialize(report),
                ReportCsvFileName.Create(report));
    }

    private static IReadOnlyList<ResolvedReportWindow> ValidateFrozenWindows(
        IReadOnlyList<ResolvedReportWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count is < 1 or > ReportWindows.MaximumWindowCount)
        {
            throw new ReportGenerationPreconditionException(
                "The frozen report window list is invalid.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Key)
                || window.Key.Length > ReportWindows.KeyMaxLength
                || window.EndDateExclusive <= window.StartDate
                || !keys.Add(window.Key))
            {
                throw new ReportGenerationPreconditionException(
                    "The frozen report window list is invalid.");
            }
        }

        return windows;
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

    private async Task<IReadOnlyList<CollectionResult>> CollectAsync(
        IReadOnlyList<KeySnapshot> keys,
        Sub2ApiConnectionCredentials credentials,
        string timezone,
        int maximumConcurrency,
        IReadOnlyList<ResolvedReportWindow> windows,
        CancellationToken cancellationToken)
    {
        var work = new List<CollectionRequest>();
        foreach (var key in keys)
        {
            foreach (var range in windows
                .Select(window => (window.StartDate, window.EndDateExclusive))
                .Distinct())
            {
                work.Add(new CollectionRequest(key, range.StartDate, range.EndDateExclusive));
            }
        }

        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = new Task<CollectionResult>[work.Count];
        for (var index = 0; index < work.Count; index++)
        {
            tasks[index] = CollectRangeAsync(
                work[index],
                credentials,
                timezone,
                semaphore,
                cancellationToken);
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<CollectionResult> CollectRangeAsync(
        CollectionRequest request,
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
                request.Key.ExternalUserId,
                request.Key.ExternalId,
                request.StartDate,
                request.EndDateExclusive.AddDays(-1),
                timezone,
                cancellationToken);
            return new CollectionResult(request, Map(stats), null);
        }
        catch (Sub2ApiClientException exception)
        {
            return new CollectionResult(request, null, exception);
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
        ReportTrigger trigger,
        DateOnly cutoffDate,
        IReadOnlyList<ResolvedReportWindow> windows,
        IReadOnlyList<KeySnapshot> keys,
        IReadOnlyList<CollectionResult> results)
    {
        var resultsByRange = results.ToDictionary(
            result => (result.Request.Key.Id, result.Request.StartDate, result.Request.EndDateExclusive));
        var keyAccumulators = new Dictionary<Guid, Dictionary<string, MetricsAccumulator>>();
        var userAccumulators = new Dictionary<Guid, UserAccumulator>();
        foreach (var key in keys)
        {
            var windowMetrics = windows.ToDictionary(
                window => window.Key,
                _ => new MetricsAccumulator(),
                StringComparer.Ordinal);
            foreach (var window in windows)
            {
                if (!resultsByRange.TryGetValue(
                        (key.Id, window.StartDate, window.EndDateExclusive),
                        out var result))
                {
                    continue;
                }

                if (result.Metrics is not null)
                {
                    windowMetrics[window.Key].Add(result.Metrics);
                }
            }

            keyAccumulators[key.Id] = windowMetrics;
            var accumulator = userAccumulators.TryGetValue(key.UserId, out var existing)
                ? existing
                : userAccumulators[key.UserId] = new UserAccumulator(
                    key.UserId,
                    key.ExternalUserId,
                    key.UserEmail,
                    key.Username);
            accumulator.AddKey(windowMetrics);
        }

        var descriptors = windows
            .Select(window => new ReportWindowDescriptor(
                window.Key,
                window.Kind,
                window.RollingDays,
                window.WeekStartsOn,
                window.StartDate,
                window.EndDateExclusive,
                window.DayCount,
                window.Label))
            .ToArray();
        var windowKeys = descriptors.Select(window => window.Key).ToArray();
        var windowTotals = descriptors
            .Select(window => new ReportWindowMetrics(
                window.Key,
                userAccumulators.Values
                    .Select(user => user.GetWindow(window.Key))
                    .Aggregate(
                        new MetricsAccumulator(),
                        (total, metrics) =>
                        {
                            total.Add(metrics.ToMetrics());
                            return total;
                        })
                    .ToMetrics()))
            .ToArray();
        var users = userAccumulators.Values
            .OrderBy(user => user.ExternalUserId)
            .Select(user => new ReportUserUsage(
                user.UserId,
                user.ExternalUserId,
                user.Username,
                user.Email,
                user.KeyCount,
                user.ToWindowMetrics(windowKeys)))
            .ToArray();
        var keyUsage = keys
            .Select(key => new ReportKeyUsage(
                key.Id,
                key.ExternalId.ToString(CultureInfo.InvariantCulture),
                key.ExternalUserId,
                key.UserEmail,
                key.Name,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt,
                windowKeys
                    .Select(windowKey => new ReportWindowMetrics(
                        windowKey,
                        keyAccumulators[key.Id][windowKey].ToMetrics()))
                    .ToArray()))
            .ToArray();
        var failedRanges = CollectFailedRanges(windows, results)
            .OrderBy(item => item.ExternalUserId)
            .ThenBy(item => item.ExternalKeyId)
            .ThenBy(item => item.WindowKey, StringComparer.Ordinal)
            .ThenBy(item => item.StartDate)
            .ToArray();
        return new ReportDocument(
            ReportSnapshot.CurrentSchemaVersion,
            reportId,
            failedRanges.Length == 0 ? ReportStatus.Complete : ReportStatus.Partial,
            trigger,
            generatedAt,
            timezone,
            connectionRevision,
            descriptors,
            windowTotals,
            users,
            keyUsage,
            new ReportDiagnostics(failedRanges));
    }

    private static IEnumerable<ReportRangeFailure> CollectFailedRanges(
        IReadOnlyList<ResolvedReportWindow> windows,
        IReadOnlyList<CollectionResult> results)
    {
        foreach (var result in results)
        {
            if (result.Exception is null)
            {
                continue;
            }

            foreach (var window in windows.Where(window =>
                window.StartDate == result.Request.StartDate
                && window.EndDateExclusive == result.Request.EndDateExclusive))
            {
                yield return new ReportRangeFailure(
                    result.Request.Key.ExternalUserId,
                    result.Request.Key.UserEmail,
                    result.Request.Key.ExternalId,
                    result.Request.Key.Name,
                    window.Key,
                    result.Request.StartDate,
                    result.Request.EndDateExclusive,
                    result.Exception.Kind,
                    DescribeFailureCode(result.Exception.Kind));
            }
        }
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

    private sealed record CollectionRequest(
        KeySnapshot Key,
        DateOnly StartDate,
        DateOnly EndDateExclusive);

    private sealed record CollectionResult(
        CollectionRequest Request,
        ReportUsageMetrics? Metrics,
        Sub2ApiClientException? Exception);

    private sealed class UserAccumulator(
        Guid userId,
        long externalUserId,
        string email,
        string? username)
    {
        private readonly Dictionary<string, MetricsAccumulator> _windows = new(StringComparer.Ordinal);

        public Guid UserId { get; } = userId;

        public long ExternalUserId { get; } = externalUserId;

        public string Email { get; } = email;

        public string? Username { get; } = username;

        public int KeyCount { get; private set; }

        public void AddKey(Dictionary<string, MetricsAccumulator> keyWindows)
        {
            KeyCount++;
            foreach (var (windowKey, metrics) in keyWindows)
            {
                if (_windows.TryGetValue(windowKey, out var existing))
                {
                    existing.Add(metrics.ToMetrics());
                }
                else
                {
                    _windows[windowKey] = new MetricsAccumulator(metrics.ToMetrics());
                }
            }
        }

        public MetricsAccumulator GetWindow(string windowKey) =>
            _windows.TryGetValue(windowKey, out var accumulator)
                ? accumulator
                : new MetricsAccumulator();

        public ReportWindowMetrics[] ToWindowMetrics(IReadOnlyList<string> windowKeys) => windowKeys
            .Select(windowKey => new ReportWindowMetrics(windowKey, GetWindow(windowKey).ToMetrics()))
            .ToArray();
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

        public MetricsAccumulator()
        {
        }

        public MetricsAccumulator(ReportUsageMetrics metrics) => Add(metrics);

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
