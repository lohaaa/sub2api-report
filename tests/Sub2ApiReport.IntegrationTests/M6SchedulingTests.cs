using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class M6SchedulingTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task ScheduleUpdateWithoutShortMonthStrategyKeepsStoredStrategy()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var original = Assert.IsType<ReportScheduleResponse>(
            await client.GetFromJsonAsync<ReportScheduleResponse>("/api/v1/schedule", JsonOptions));
        Assert.Equal(ShortMonthStrategy.UseLastDay, original.ShortMonthStrategy);

        // Older SPA payloads omit the strategy field; saving must not be rejected.
        using var legacyResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/schedule",
            new
            {
                enabled = true,
                dayOfMonth = 12,
                localTime = "10:30",
                timezone = "UTC",
                revision = original.Revision,
            });
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        var legacy = Assert.IsType<ReportScheduleResponse>(
            await legacyResponse.Content.ReadFromJsonAsync<ReportScheduleResponse>(JsonOptions));
        Assert.Equal(ShortMonthStrategy.UseLastDay, legacy.ShortMonthStrategy);
        Assert.True(legacy.Synchronized);
        Assert.NotNull(legacy.NextRunAt);

        // A newer client switches the strategy on a day beyond the short months.
        using var strategyResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/schedule",
            new
            {
                enabled = true,
                dayOfMonth = 31,
                shortMonthStrategy = "SkipMonth",
                localTime = "08:00",
                timezone = "UTC",
                revision = legacy.Revision,
            });
        Assert.Equal(HttpStatusCode.OK, strategyResponse.StatusCode);
        var withStrategy = Assert.IsType<ReportScheduleResponse>(
            await strategyResponse.Content.ReadFromJsonAsync<ReportScheduleResponse>(JsonOptions));
        Assert.Equal(ShortMonthStrategy.SkipMonth, withStrategy.ShortMonthStrategy);
        Assert.Equal(31, withStrategy.DayOfMonth);

        // A follow-up legacy save keeps the previously stored strategy.
        using var secondLegacyResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/schedule",
            new
            {
                enabled = false,
                dayOfMonth = 3,
                localTime = "10:30",
                timezone = "UTC",
                revision = withStrategy.Revision,
            });
        Assert.Equal(HttpStatusCode.OK, secondLegacyResponse.StatusCode);
        var keptStrategy = Assert.IsType<ReportScheduleResponse>(
            await secondLegacyResponse.Content.ReadFromJsonAsync<ReportScheduleResponse>(JsonOptions));
        Assert.Equal(ShortMonthStrategy.SkipMonth, keptStrategy.ShortMonthStrategy);
    }

    [Fact]
    public async Task ScheduleUpdateSynchronizesPersistentTriggerAndRejectsStaleRevision()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var original = Assert.IsType<ReportScheduleResponse>(
            await client.GetFromJsonAsync<ReportScheduleResponse>("/api/v1/schedule", JsonOptions));
        Assert.False(original.Enabled);
        Assert.True(original.Synchronized);
        Assert.Null(original.NextRunAt);

        using var updatedResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/schedule",
            new
            {
                enabled = true,
                dayOfMonth = 12,
                localTime = "10:30",
                timezone = "UTC",
                revision = original.Revision,
            });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = Assert.IsType<ReportScheduleResponse>(
            await updatedResponse.Content.ReadFromJsonAsync<ReportScheduleResponse>(JsonOptions));
        Assert.True(updated.Enabled);
        Assert.True(updated.Synchronized);
        Assert.NotNull(updated.NextRunAt);
        Assert.Equal(original.Revision + 1, updated.Revision);

        using var staleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/schedule",
            new
            {
                enabled = false,
                dayOfMonth = 1,
                localTime = "09:00",
                timezone = "Asia/Shanghai",
                revision = original.Revision,
            });
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var stored = await dbContext.ReportSchedules.FindAsync(ReportSchedule.SingletonId);
        Assert.NotNull(stored);
        Assert.True(stored.Enabled);
        Assert.Equal(12, stored.DayOfMonth);
        Assert.Contains(dbContext.AuditEvents, item => item.Action == "report.schedule.update");
    }

    [Fact]
    public async Task RunNowCreatesTerminalHistoryAndRetryCreatesLinkedAttempt()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

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
        Assert.Equal(1, queued.Attempt);

        var failed = await WaitForTerminalAsync(client, queued.Id);
        Assert.Equal(ReportRunStatus.Failed, failed.Status);
        Assert.True(failed.CanRetry);
        Assert.Equal("connection_not_configured", failed.ErrorCode);
        Assert.Equal("Sub2API 连接尚未配置。", failed.ErrorMessage);
        Assert.Null(failed.ReportId);

        using var retryResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/schedule/runs/{failed.Id:D}/retry",
            new { confirmOutcomeUnknown = false });
        Assert.Equal(HttpStatusCode.Accepted, retryResponse.StatusCode);
        var retry = Assert.IsType<ReportTaskRunResponse>(
            await retryResponse.Content.ReadFromJsonAsync<ReportTaskRunResponse>(JsonOptions));
        Assert.Equal(ReportRunTrigger.Retry, retry.Trigger);
        Assert.Equal(failed.Id, retry.RetryOfRunId);
        Assert.Equal(2, retry.Attempt);
        Assert.NotEqual(failed.Id, retry.Id);

        var retriedFailure = await WaitForTerminalAsync(client, retry.Id);
        Assert.Equal(ReportRunStatus.Failed, retriedFailure.Status);
        Assert.Equal(failed.Id, retriedFailure.RetryOfRunId);
    }

    [Fact]
    public async Task StartupMarksInterruptedGenerationHistoryAsFailed()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-m6-recovery-{Guid.NewGuid():N}.db");
        var generationId = Guid.Empty;
        try
        {
            await using (var firstFactory = new ApiWebApplicationFactory(
                databasePath,
                deleteDatabaseOnDispose: false))
            {
                await using var scope = firstFactory.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
                var generation = ReportGenerationRun.Start(
                    ReportTrigger.ManualDryRun,
                    1,
                    DateTimeOffset.UtcNow);
                generationId = generation.Id;
                dbContext.ReportGenerationRuns.Add(generation);
                await dbContext.SaveChangesAsync();
            }

            await using var secondFactory = new ApiWebApplicationFactory(
                databasePath,
                deleteDatabaseOnDispose: false);
            await using var recoveryScope = secondFactory.Services.CreateAsyncScope();
            var recoveredDbContext = recoveryScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var recovered = await recoveredDbContext.ReportGenerationRuns
                .AsNoTracking()
                .SingleAsync(item => item.Id == generationId);
            Assert.Equal(ReportGenerationStatus.Failed, recovered.Status);
            Assert.Equal("interrupted", recovered.Stage);
            Assert.Equal("interrupted", recovered.ErrorCode);
            Assert.NotNull(recovered.CompletedAt);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    [Fact]
    public async Task StartupFailsTaskRunsInterruptedDuringGeneration()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-m6-task-recovery-{Guid.NewGuid():N}.db");
        try
        {
            var renderingRunId = Guid.Empty;
            var queuedRunId = Guid.Empty;
            var deliveringRunId = Guid.Empty;
            await using (var firstFactory = new ApiWebApplicationFactory(
                databasePath,
                deleteDatabaseOnDispose: false))
            {
                await using var scope = firstFactory.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
                var now = DateTimeOffset.UtcNow;

                // 与用户事故一致的残留态：快照已持久化（ReportSnapshotId 非空），
                // 但任务从未离开“生成快照”阶段。
                var renderingRun = ReportRun.QueueManualScheduled(
                    ReportSchedule.SingletonId,
                    1,
                    new DateOnly(2026, 8, 30),
                    "UTC",
                    null,
                    null,
                    now);
                renderingRun.BeginCollecting(now);
                renderingRun.BeginRendering(now);
                var snapshot = ReportSnapshot.Create(
                    Guid.NewGuid(),
                    ReportStatus.Complete,
                    ReportTrigger.ManualScheduled,
                    new DateOnly(2026, 8, 30),
                    "UTC",
                    1,
                    now,
                    1,
                    1,
                    0,
                    0m,
                    0m,
                    null,
                    "{\"schemaVersion\":1}");
                dbContext.ReportSnapshots.Add(snapshot);
                renderingRun.AttachSnapshot(snapshot.Id);
                dbContext.ReportRuns.Add(renderingRun);

                var queuedRun = ReportRun.QueueManualScheduled(
                    ReportSchedule.SingletonId,
                    1,
                    new DateOnly(2026, 8, 30),
                    "UTC",
                    null,
                    null,
                    now);
                dbContext.ReportRuns.Add(queuedRun);

                // Delivering 交由 Quartz 恢复路径，启动收敛必须保留它。
                var deliveringRun = ReportRun.QueueManualScheduled(
                    ReportSchedule.SingletonId,
                    1,
                    new DateOnly(2026, 8, 30),
                    "UTC",
                    null,
                    null,
                    now);
                deliveringRun.BeginCollecting(now);
                deliveringRun.BeginRendering(now);
                deliveringRun.AttachSnapshot(snapshot.Id);

                var channel = NotificationChannel.Create(
                    NotificationChannelType.Email,
                    "邮件渠道",
                    true,
                    new ChannelSettings.Email(
                        "smtp.example.com",
                        587,
                        SmtpSecurityMode.StartTls,
                        null,
                        "reports@example.com",
                        "Sub2API Report",
                        ["to@example.com"],
                        []),
                    new ChannelSecretCiphertexts(
                        SmtpPasswordCiphertext: "encrypted-password",
                        SmtpPasswordSuffix: "ord1"),
                    now);
                dbContext.NotificationChannels.Add(channel);

                var delivery = DeliveryRecord.Create(
                    channel.Id,
                    NotificationChannelType.Email,
                    channel.Name,
                    "payload-hash",
                    [DeliveryPart.Create(0, 1, "part-hash")]);
                delivery.MarkSending();
                deliveringRun.Deliveries.Add(delivery);
                deliveringRun.BeginDelivering(now);
                dbContext.ReportRuns.Add(deliveringRun);

                await dbContext.SaveChangesAsync();
                renderingRunId = renderingRun.Id;
                queuedRunId = queuedRun.Id;
                deliveringRunId = deliveringRun.Id;
            }

            await using var secondFactory = new ApiWebApplicationFactory(
                databasePath,
                deleteDatabaseOnDispose: false);
            await using var recoveryScope = secondFactory.Services.CreateAsyncScope();
            var recoveredDbContext = recoveryScope.ServiceProvider.GetRequiredService<ReportDbContext>();

            var recoveredRendering = await recoveredDbContext.ReportRuns
                .AsNoTracking()
                .SingleAsync(item => item.Id == renderingRunId);
            Assert.Equal(ReportRunStatus.Failed, recoveredRendering.Status);
            Assert.Equal("interrupted", recoveredRendering.ErrorCode);
            Assert.Equal("任务在应用重启前未完成。", recoveredRendering.ErrorMessage);
            Assert.NotNull(recoveredRendering.CompletedAt);
            Assert.True(recoveredRendering.IsTaskRetryable);

            var recoveredQueued = await recoveredDbContext.ReportRuns
                .AsNoTracking()
                .SingleAsync(item => item.Id == queuedRunId);
            Assert.Equal(ReportRunStatus.Failed, recoveredQueued.Status);
            Assert.Equal("interrupted", recoveredQueued.ErrorCode);

            var recoveredDelivering = await recoveredDbContext.ReportRuns
                .Include(item => item.Deliveries)
                .SingleAsync(item => item.Id == deliveringRunId);
            Assert.Equal(ReportRunStatus.Delivering, recoveredDelivering.Status);
            Assert.NotEqual("interrupted", recoveredDelivering.ErrorCode);
            Assert.All(recoveredDelivering.Deliveries, delivery =>
                Assert.Equal(DeliveryStatus.Sending, delivery.Status));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    [Fact]
    public async Task ScheduledIdempotencyKeyIsEnforcedBySqlite()
    {
        await using var factory = new ApiWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var now = DateTimeOffset.UtcNow;
        const string idempotencyKey = "scheduled:1:2026-08-26";
        dbContext.ReportRuns.Add(ReportRun.QueueScheduled(
            ReportSchedule.SingletonId,
            1,
            new DateOnly(2026, 8, 26),
            "UTC",
            null,
            null,
            idempotencyKey,
            now));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        dbContext.ReportRuns.Add(ReportRun.QueueScheduled(
            ReportSchedule.SingletonId,
            1,
            new DateOnly(2026, 8, 26),
            "UTC",
            null,
            null,
            idempotencyKey,
            now.AddSeconds(1)));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
    }

    private static async Task<ReportTaskRunResponse> WaitForTerminalAsync(
        HttpClient client,
        Guid runId)
    {
        // Poll with a generous deadline (300 x 100 ms) so slow CI never turns into a flaky
        // timeout; genuine failures still exit with the timeout exception below.
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
}
