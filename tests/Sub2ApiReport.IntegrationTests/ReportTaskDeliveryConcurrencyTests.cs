using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;
using Sub2ApiReport.Infrastructure.Scheduling;

namespace Sub2ApiReport.IntegrationTests;

/// <summary>
/// Exercises the full scheduled task pipeline (RunNow → sync → collect → snapshot → delivery)
/// against real SQLite storage with enabled stub channels, asserting that runs reach terminal
/// states with delivery records instead of getting stuck after the snapshot stage.
/// </summary>
public sealed class ReportTaskDeliveryConcurrencyTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private const string SyntheticWebhookUrl =
        "https://oapi.dingtalk.com/robot/send?access_token=synthetic-access-token";
    private const string SyntheticSignSecret = "synthetic-sign-secret-1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] RecipientAddresses = ["recipient@example.com"];

    [Fact]
    public async Task RunNowReachesSucceededWithTwoChannelDeliveriesAndSnapshot()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        var emailSender = new StubReportSender(NotificationChannelType.Email);
        await using var factory = CreateFactory(dingTalkSender, emailSender);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var dingTalk = await CreateDingTalkChannelAsync(client);
        var email = await CreateEmailChannelAsync(client);
        await ConfigureAndSynchronizeAsync(client);

        using var runResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/schedule/run",
            null);
        Assert.Equal(HttpStatusCode.Accepted, runResponse.StatusCode);
        var queued = Assert.IsType<ReportTaskRunResponse>(
            await runResponse.Content.ReadFromJsonAsync<ReportTaskRunResponse>(JsonOptions));
        Assert.Equal(ReportRunTrigger.ManualScheduled, queued.Trigger);
        Assert.Equal(ReportRunStatus.Queued, queued.Status);

        var terminal = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(ReportRunStatus.Succeeded, terminal.Status);
        Assert.NotNull(terminal.ReportId);
        Assert.Null(terminal.ErrorCode);
        Assert.Equal(2, terminal.DeliveryCount);
        Assert.Equal(2, terminal.SucceededDeliveryCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var stored = await dbContext.ReportRuns
            .AsNoTracking()
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .SingleAsync(item => item.Id == queued.Id);
        Assert.Equal(ReportRunStatus.Succeeded, stored.Status);
        Assert.NotNull(stored.ReportSnapshotId);
        Assert.True(await dbContext.ReportSnapshots.AnyAsync(item => item.Id == stored.ReportSnapshotId));
        Assert.Equal(2, stored.Deliveries.Count);
        Assert.All(stored.Deliveries, delivery =>
        {
            Assert.Equal(DeliveryStatus.Succeeded, delivery.Status);
            Assert.Single(delivery.Parts);
            Assert.Equal(DeliveryPartStatus.Succeeded, Assert.Single(delivery.Parts).Status);
        });
        Assert.Equal(1, dingTalkSender.SendCount);
        Assert.Equal(1, emailSender.SendCount);
    }

    [Fact]
    public async Task SynchronousExecutorCompletesDeliveryDespiteConcurrentRevisionWrites()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        var emailSender = new StubReportSender(NotificationChannelType.Email);
        var upstream = new StubSub2ApiClient
        {
            Keys = [new Sub2ApiExternalKey(101, "Synthetic Key", "active", 7, null)],
        };
        Guid runId = Guid.Empty;
        await using var factory = CreateFactory(dingTalkSender, emailSender, upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        _ = await CreateDingTalkChannelAsync(client);
        _ = await CreateEmailChannelAsync(client);
        await ConfigureAndSynchronizeAsync(client);

        // 第二个 DbContext 在采集统计阶段与投递保存之间并发改写
        // NotificationChannels.Revision 乐观并发行。该渠道行会被投递阶段跟踪读取；
        // 修复后的阶段边界（清理 ChangeTracker + 按 runId 重加载）保证并发写入
        // 不会被任务上下文覆盖，整个同步 RunNow 流程仍然成功。
        var channelsBeforeRun = Assert.IsType<List<ChannelResponse>>(
            await client.GetFromJsonAsync<List<ChannelResponse>>("/api/v1/channels", JsonOptions));
        upstream.StatsCallback = async (_, cancellationToken) =>
        {
            if (upstream.StatsCallCount > 1)
            {
                return;
            }

            await using var writerScope = factory.Services.CreateAsyncScope();
            var writer = writerScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            await writer.Database.ExecuteSqlRawAsync(
                "UPDATE NotificationChannels SET Revision = Revision + 1",
                cancellationToken);
        };

        runId = await CreateQueuedRunAndExecuteAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var stored = await dbContext.ReportRuns
            .AsNoTracking()
            .Include(item => item.Deliveries)
            .SingleAsync(item => item.Id == runId);
        Assert.Equal(ReportRunStatus.Succeeded, stored.Status);
        Assert.NotNull(stored.ReportSnapshotId);
        Assert.Equal(2, stored.Deliveries.Count);

        // 并发写方的 Revision 增量必须原样保留（任务上下文不得回写失效值）。
        foreach (var channelBefore in channelsBeforeRun)
        {
            var channelAfter = Assert.Single(
                await dbContext.NotificationChannels.AsNoTracking()
                    .Where(item => item.Id == channelBefore.Id)
                    .ToListAsync(),
                item => true);
            Assert.Equal(channelBefore.Revision + 1, channelAfter.Revision);
        }
    }

    [Fact]
    public async Task ExecutorRecordsTerminalFailureWhenDeliveryStageThrows()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        var emailSender = new StubReportSender(NotificationChannelType.Email);
        Guid runId = Guid.Empty;
        await using var factory = CreateFactory(
              dingTalkSender,
              emailSender,
              configureTestServices: services =>
              {
                  services.RemoveAll<IReportDeliveryService>();
                  services.AddScoped<IReportDeliveryService>(serviceProvider =>
                      new ThrowingDeliveryService(serviceProvider.GetRequiredService<ReportDbContext>()));
              });
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        _ = await CreateDingTalkChannelAsync(client);
        _ = await CreateEmailChannelAsync(client);
        await ConfigureAndSynchronizeAsync(client);

        runId = await CreateQueuedRunAndExecuteAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var stored = await dbContext.ReportRuns
            .AsNoTracking()
            .Include(item => item.Deliveries)
            .SingleAsync(item => item.Id == runId);
        Assert.Equal(ReportRunStatus.Failed, stored.Status);
        Assert.Equal("delivery_precondition", stored.ErrorCode);
        Assert.NotNull(stored.ReportSnapshotId);
        Assert.True(await dbContext.ReportSnapshots.AnyAsync(item => item.Id == stored.ReportSnapshotId));
        Assert.Empty(stored.Deliveries);
        Assert.True(stored.CompletedAt is not null && stored.IsTaskRetryable);
    }

    [Fact]
    public async Task TrackedRunCollectionAddReproducesOriginalConflictAndExplicitAddIsAdded()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-tracker-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        try
        {
            Guid runId;
            Guid channelId;
            await using (var context = new ReportDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                var channel = NotificationChannel.Create(
                    NotificationChannelType.DingTalk,
                    "synthetic-channel",
                    enabled: true,
                    new ChannelSettings.DingTalk(),
                    new ChannelSecretCiphertexts(
                        WebhookCiphertext: "synthetic-webhook-ciphertext",
                        WebhookSuffix: "ynt-1",
                        SignSecretCiphertext: "synthetic-sign-ciphertext",
                        SignSecretSuffix: "sig-1"),
                    Now);
                context.NotificationChannels.Add(channel);
                var run = ReportRun.QueueManualScheduled(
                    1,
                    1,
                    new DateOnly(2026, 8, 25),
                    "UTC",
                    null,
                    null,
                    Now);
                context.ReportRuns.Add(run);
                await context.SaveChangesAsync();
                runId = run.Id;
                channelId = channel.Id;
            }

            // 生产事故复现：对已跟踪（非 Added）run 使用集合导航添加新投递图时，EF Core
            // 会把新 DeliveryRecord/DeliveryPart 判定为 Modified；SaveChanges 生成 UPDATE
            // 匹配 0 行，抛出 DbUpdateConcurrencyException。这条链路就是线上快照后卡死根因。
            await using (var context = new ReportDbContext(options))
            {
                var run = await context.ReportRuns.SingleAsync(item => item.Id == runId);
                var delivery = DeliveryRecord.Create(
                    channelId,
                    NotificationChannelType.DingTalk,
                    "synthetic-channel",
                    DeliveryPayloadHash.Compute("subject", "body", null),
                    [DeliveryPart.Create(0, 1, DeliveryPayloadHash.Compute("subject", "body", null))]);
                run.Deliveries.Add(delivery);

                var states = context.ChangeTracker.Entries().ToList();
                Assert.Contains(states, item =>
                    item.State == EntityState.Modified && item.Entity is DeliveryRecord);
                var partEntry = Assert.Single(states, item => item.Entity is DeliveryPart);
                Assert.Equal(EntityState.Modified, partEntry.State);

                var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                    context.SaveChangesAsync());
                var conflicted = conflict.Entries.Select(item => item.Entity).ToArray();
                Assert.Contains(conflicted, entity => entity is DeliveryPart);
            }

            // 修复后的模式：显式主外键 + DbSet.Add，整个新投递图一律按 Added 追踪并成功落库。
            await using (var context = new ReportDbContext(options))
            {
                var run = await context.ReportRuns.SingleAsync(item => item.Id == runId);
                var deliveryId = Guid.NewGuid();
                var delivery = DeliveryRecord.Create(
                    channelId,
                    NotificationChannelType.DingTalk,
                    "synthetic-channel",
                    DeliveryPayloadHash.Compute("subject", "body", null),
                    [DeliveryPart.Create(0, 1, DeliveryPayloadHash.Compute("subject", "body", null), deliveryId)],
                    deliveryId,
                    run.Id);
                context.DeliveryRecords.Add(delivery);

                var trackedStates = context.ChangeTracker.Entries().ToList();
                Assert.All(trackedStates, item =>
                    Assert.NotEqual(EntityState.Modified, item.State));
                Assert.Contains(trackedStates, item =>
                    item.State == EntityState.Added && item.Entity is DeliveryRecord);
                Assert.Contains(trackedStates, item =>
                    item.State == EntityState.Added && item.Entity is DeliveryPart);

                await context.SaveChangesAsync();
                Assert.Single(run.Deliveries);
                var stored = await context.DeliveryRecords.AsNoTracking()
                    .Include(item => item.Parts)
                    .SingleAsync(item => item.RunId == runId);
                Assert.Equal(runId, stored.RunId);
                Assert.Equal(DeliveryStatus.Pending, stored.Status);
                Assert.Single(stored.Parts);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    private static async Task<Guid> CreateQueuedRunAndExecuteAsync(ApiWebApplicationFactory factory)
    {
        Guid runId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var schedule = await setupDb.ReportSchedules.AsNoTracking().SingleAsync();
            var periodEnd = new DateOnly(2026, 8, 25);
            var (specsJson, resolvedJson) = DatabaseReportScheduleService.FreezeWindows(
                schedule.WindowSpecsJson, periodEnd);
            var run = ReportRun.QueueManualScheduled(
                schedule.Id,
                schedule.Revision,
                periodEnd,
                schedule.Timezone,
                specsJson,
                resolvedJson,
                Now);
            setupDb.ReportRuns.Add(run);
            await setupDb.SaveChangesAsync();
            runId = run.Id;
        }

        await using var jobScope = factory.Services.CreateAsyncScope();
        var executor = jobScope.ServiceProvider.GetRequiredService<IReportTaskExecutor>();
        await executor.ExecuteAsync(runId, recovering: false, CancellationToken.None);
        return runId;
    }

    private static ApiWebApplicationFactory CreateFactory(
        StubReportSender dingTalkSender,
        StubReportSender? emailSender = null,
        StubSub2ApiClient? upstream = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        var upstreamClient = upstream ?? new StubSub2ApiClient
        {
            Keys = [new Sub2ApiExternalKey(101, "Synthetic Key", "active", 7, null)],
        };
        return new ApiWebApplicationFactory(
            databasePath: null,
            deleteDatabaseOnDispose: true,
            configureTestServices: services =>
            {
                services.RemoveAll<ISub2ApiClient>();
                services.RemoveAll<IReportSender>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<ISub2ApiClient>(upstreamClient);
                services.AddSingleton<IReportSender>(dingTalkSender);
                services.AddSingleton<IReportSender>(emailSender
                    ?? new StubReportSender(NotificationChannelType.Email));
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                configureTestServices?.Invoke(services);
            });
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task InitializeAsync(ApiWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setupService = scope.ServiceProvider.GetRequiredService<ISetupService>();
        var issue = Assert.IsType<SecretCodeIssue>(await setupService.RotateChallengeOnStartupAsync(
            CancellationToken.None));
        using var client = CreateClient(factory);
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = issue.Code, username = Username, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "/api/v1/security/antiforgery");
        Assert.NotNull(token);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", token.Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<ReportTaskRunResponse> WaitForTerminalAsync(
        HttpClient client,
        Guid runId)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var page = Assert.IsType<ReportTaskRunPageResponse>(
                await client.GetFromJsonAsync<ReportTaskRunPageResponse>(
                    "/api/v1/schedule/runs?page=1&pageSize=20",
                    JsonOptions));
            var run = page.Items.SingleOrDefault(item => item.Id == runId);
            if (run?.Status is ReportRunStatus.Succeeded
                or ReportRunStatus.PartialFailed
                or ReportRunStatus.Failed)
            {
                return run;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Report task {runId} did not reach a terminal state.");
    }

    private static async Task ConfigureAndSynchronizeAsync(HttpClient client)
    {
        await StepUpAsync(client);
        using var saveResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/connection",
            new
            {
                baseUrl = "https://sub2api.example.com",
                adminApiKey = "synthetic-sub2api-admin-key-1234",
                clearAdminApiKey = false,
                codexGroupId = "7",
                revision = 0,
            });
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        await SynchronizeUsersAndSelectFirstAsync(client);
        using var response = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/sub2api/keys/sync",
            null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SynchronizeUsersAndSelectFirstAsync(HttpClient client)
    {
        using var sync = await SendJsonAsync<object?>(client, HttpMethod.Post, "/api/v1/sub2api/users/sync", null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        var scope = Assert.IsType<Sub2ApiUserScopeResponse>(
            await client.GetFromJsonAsync<Sub2ApiUserScopeResponse>("/api/v1/sub2api/users"));
        var user = Assert.Single(scope.Users);
        using var update = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/users/scope",
            new { mode = "SelectedUsers", selectedUserIds = new[] { user.Id }, revision = scope.ConnectionRevision });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    private static async Task StepUpAsync(HttpClient client)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/step-up",
            new { password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<ChannelResponse> CreateDingTalkChannelAsync(HttpClient client)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/channels",
            new
            {
                type = "DingTalk",
                name = "合成钉钉渠道",
                enabled = true,
                email = (object?)null,
                smtpPassword = (string?)null,
                webhookUrl = SyntheticWebhookUrl,
                signSecret = SyntheticSignSecret,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<ChannelResponse>(
            await response.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions));
    }

    private static async Task<ChannelResponse> CreateEmailChannelAsync(HttpClient client)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/channels",
            new
            {
                type = "Email",
                name = "合成邮件渠道",
                enabled = true,
                email = new
                {
                    host = "smtp.example.com",
                    port = 587,
                    security = "StartTls",
                    username = "reports@example.com",
                    fromAddress = "reports@example.com",
                    fromName = "Sub2API Report",
                    toAddresses = RecipientAddresses,
                    ccAddresses = Array.Empty<string>(),
                },
                smtpPassword = (string?)null,
                webhookUrl = (string?)null,
                signSecret = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<ChannelResponse>(
            await response.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions));
    }

    private sealed class ThrowingDeliveryService(ReportDbContext dbContext) : IReportDeliveryService
    {
        public async Task<DeliveryRunDocument> DeliverAsync(
            DeliverReportCommand command,
            CancellationToken cancellationToken)
        {
            throw new ReportDeliveryPreconditionException("synthetic delivery failure");
        }

        public async Task<DeliveryRunDocument> RetryAsync(
            RetryDeliveryCommand command,
            CancellationToken cancellationToken)
        {
            throw new ReportDeliveryPreconditionException("synthetic delivery failure");
        }

        public async Task<DeliveryRunDocument> DeliverTaskAsync(
            DeliverReportTaskCommand command,
            CancellationToken cancellationToken)
        {
            // 模拟 BeginDelivering 前的真实并发冲突：run 已被另一写方推进。
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE ReportRuns SET Status = 'Delivering', "
                + "DeliveringAtUnixMilliseconds = strftime('%s','now') * 1000 WHERE Id = {0}",
                [command.RunId], cancellationToken);
            throw new ReportDeliveryPreconditionException("synthetic task delivery failure");
        }

        public async Task<IReadOnlyList<DeliveryRunDocument>> GetRunsAsync(
            Guid reportId,
            CancellationToken cancellationToken)
        {
            return [];
        }
    }

    internal sealed class StubReportSender(NotificationChannelType type) : IReportSender
    {
        private int _sendCount;

        public NotificationChannelType ChannelType => type;

        public Func<string, ChannelSendOutcome>? OutcomeFor { get; set; }

        public int SendCount => Volatile.Read(ref _sendCount);

        public IReadOnlyList<OutboundPart> Render(
            ReportDocument report,
            ChannelDeliveryContext context)
        {
            var body = $"统计窗口（合成）\n渠道 {context.ChannelName}";
            return
            [
                new OutboundPart(
                    0,
                    1,
                    "[合成] Codex 用量报告",
                    body,
                    null,
                    DeliveryPayloadHash.Compute("subject", body, null)),
            ];
        }

        public Task<ChannelSendOutcome> SendPartAsync(
            OutboundPart part,
            ChannelDeliveryContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            var outcome = OutcomeFor?.Invoke(context.ChannelName) ?? ChannelSendOutcome.Ok;
            return Task.FromResult(outcome);
        }

        public Task<ChannelSendOutcome> SendTestAsync(
            ChannelDeliveryContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            var outcome = OutcomeFor?.Invoke(context.ChannelName) ?? ChannelSendOutcome.Ok;
            return Task.FromResult(outcome);
        }
    }

    internal sealed class StubSub2ApiClient : ISub2ApiClient
    {
        public Sub2ApiExternalKey[] Keys { get; set; } = [];

        private int _statsCallCount;

        public int StatsCallCount => Volatile.Read(ref _statsCallCount);

        public Func<int, CancellationToken, Task>? StatsCallback { get; set; }

        public Task<Sub2ApiConnectionProbe> TestAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Sub2ApiConnectionProbe(Keys.Length));

        public Task<IReadOnlyList<Sub2ApiExternalUser>> GetUsersAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Sub2ApiExternalUser>>(
                [new Sub2ApiExternalUser(42, "user@example.com", "synthetic-user", "active")]);

        public Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
            Sub2ApiConnectionCredentials connection,
            long externalUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Sub2ApiExternalKey>>(Keys);

        public async Task<Sub2ApiUsageStats> GetUsageStatsAsync(
            Sub2ApiConnectionCredentials connection,
            long externalUserId,
            long externalApiKeyId,
            DateOnly startDate,
            DateOnly endDate,
            string timezone,
            CancellationToken cancellationToken)
        {
            var callCount = Interlocked.Increment(ref _statsCallCount);
            if (StatsCallback is { } callback)
            {
                await callback(callCount, cancellationToken);
            }

            var days = endDate.DayNumber - startDate.DayNumber + 1;
            return new Sub2ApiUsageStats(
                days,
                days * 100L,
                days * 50L,
                days * 25L,
                days * 10L,
                days * 15L,
                days * 175L,
                days * 0.2m,
                days * 0.1m,
                125.5m);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
