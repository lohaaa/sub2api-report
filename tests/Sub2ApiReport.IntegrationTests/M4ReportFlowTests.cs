using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class M4ReportFlowTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private const string AdminApiKey = "synthetic-sub2api-admin-key-1234";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportEndpointsRequireAuthenticationAndAntiforgery()
    {
        await using var factory = CreateFactory(new StubSub2ApiClient());
        using var anonymousClient = CreateClient(factory);

        using var anonymousList = await anonymousClient.GetAsync("/api/v1/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        using var missingAntiforgery = await client.PostAsJsonAsync(
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2026-08-25" });
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
    }

    [Fact]
    public async Task DryRunAutoRefreshesPersistsCanonicalSnapshotAndExportsBomCsv()
    {
        var upstream = new StubSub2ApiClient
        {
            Keys = [Key(101, "Rotated Key")],
        };
        await using var factory = CreateFactory(upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        await ConfigureConnectionAsync(client);

        using var generateResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2026-08-25" });
        Assert.Equal(HttpStatusCode.Created, generateResponse.StatusCode);
        var report = Assert.IsType<ReportDetailResponse>(
            await generateResponse.Content.ReadFromJsonAsync<ReportDetailResponse>(JsonOptions));
        Assert.Equal($"/api/v1/reports/{report.ReportId:D}", generateResponse.Headers.Location?.OriginalString);
        Assert.Equal(ReportStatus.Complete, report.Status);
        Assert.Equal(3, report.SchemaVersion);
        Assert.Equal(new DateOnly(2026, 8, 19), report.SevenDayWindow.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 27), report.ThirtyDayWindow.StartDate);
        Assert.Equal("7", report.SevenDayTotal.TotalRequests);
        Assert.Equal("30", report.ThirtyDayTotal.TotalRequests);
        Assert.Equal("3", report.ThirtyDayTotal.TotalActualCost);
        Assert.Equal(2, upstream.Calls.Count);
        Assert.All(upstream.Calls, call => Assert.Equal("Asia/Shanghai", call.Timezone));

        var user = Assert.Single(report.Users);
        Assert.Equal("user@example.com", user.Email);
        Assert.Equal(1, user.KeyCount);
        Assert.Equal("30", user.ThirtyDay.TotalRequests);
        var key = Assert.Single(report.Keys);
        Assert.Equal("Rotated Key", key.Name);
        Assert.Equal("7", key.SevenDay.TotalRequests);
        Assert.Equal("30", key.ThirtyDay.TotalRequests);
        Assert.Empty(report.Diagnostics.FailedRanges);

        using var listResponse = await client.GetAsync("/api/v1/reports?page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = Assert.IsType<ReportPageResponse>(
            await listResponse.Content.ReadFromJsonAsync<ReportPageResponse>(JsonOptions));
        Assert.Equal(1, page.Total);
        var listItem = Assert.Single(page.Items);
        Assert.Equal(report.ReportId, listItem.Id);
        Assert.Equal(1, listItem.UserCount);
        Assert.Equal(1, listItem.KeyCount);

        using var detailResponse = await client.GetAsync($"/api/v1/reports/{report.ReportId:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var persisted = Assert.IsType<ReportDetailResponse>(
            await detailResponse.Content.ReadFromJsonAsync<ReportDetailResponse>(JsonOptions));
        Assert.Equal(
            JsonSerializer.Serialize(report, JsonOptions),
            JsonSerializer.Serialize(persisted, JsonOptions));

        using var csvResponse = await client.GetAsync($"/api/v1/reports/{report.ReportId:D}/csv");
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsByteArrayAsync();
        Assert.True(csv.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var csvText = Encoding.UTF8.GetString(csv[Encoding.UTF8.GetPreamble().Length..]);
        Assert.Contains("Sub2API 用户,Key 名称,Key ID,状态", csvText, StringComparison.Ordinal);
        Assert.Contains("user@example.com,Rotated Key,101,active", csvText, StringComparison.Ordinal);
        Assert.Contains("TOTAL,全部总计", csvText, StringComparison.Ordinal);

        string originalCanonicalJson;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var stored = Assert.Single(dbContext.ReportSnapshots);
            originalCanonicalJson = stored.CanonicalJson;
            Assert.DoesNotContain(AdminApiKey, stored.CanonicalJson, StringComparison.Ordinal);
            Assert.Equal(3m, stored.ThirtyDayActualCost);
        }

        upstream.Keys = [Key(101, "Rotated Key"), Key(102, "New Key")];
        upstream.FailurePredicate = call =>
            call.ExternalApiKeyId == 101 && call.StartDate == new DateOnly(2026, 8, 19);
        using var partialResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2026-08-25" });
        Assert.Equal(HttpStatusCode.Created, partialResponse.StatusCode);
        var partial = Assert.IsType<ReportDetailResponse>(
            await partialResponse.Content.ReadFromJsonAsync<ReportDetailResponse>(JsonOptions));
        Assert.Equal(ReportStatus.Partial, partial.Status);
        Assert.Single(partial.Diagnostics.FailedRanges);
        Assert.Equal(2, partial.Keys.Count);
        Assert.Equal(2, upstream.Calls.Count(call => call.ExternalApiKeyId == 102));

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(2, verificationContext.ReportSnapshots.Count());
        Assert.Equal(
            originalCanonicalJson,
            verificationContext.ReportSnapshots.Single(item => item.Id == report.ReportId).CanonicalJson);
        Assert.Contains(verificationContext.AuditEvents, audit => audit is
        { Action: "report.generate.dry-run", Result: "succeeded" });
        Assert.Contains(verificationContext.ReportGenerationRuns, item => item.Status == ReportGenerationStatus.Succeeded);
    }

    [Fact]
    public async Task DryRunRecordsRefreshFailuresForBackgroundDisplay()
    {
        var upstream = new StubSub2ApiClient
        {
            Keys = [Key(101, "Alpha")],
        };
        await using var factory = CreateFactory(upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        await ConfigureConnectionAsync(client);

        upstream.Failure = new Sub2ApiClientException(
            Sub2ApiFailureKind.Unavailable,
            "synthetic refresh failure");

        using var failedResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2026-08-25" });
        Assert.Equal(HttpStatusCode.BadGateway, failedResponse.StatusCode);

        using var runsResponse = await client.GetAsync("/api/v1/reports/generations");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
        var runs = Assert.IsType<ReportGenerationRunPageResponse>(
            await runsResponse.Content.ReadFromJsonAsync<ReportGenerationRunPageResponse>(JsonOptions));
        var failedRun = Assert.Single(runs.Items);
        Assert.Equal(ReportGenerationStatus.Failed, failedRun.Status);
        Assert.Equal("user_sync", failedRun.Stage);
        Assert.Equal("unavailable", failedRun.ErrorCode);
        Assert.Null(failedRun.ReportSnapshotId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Empty(dbContext.ReportSnapshots);
    }

    [Fact]
    public async Task DryRunWindowsIncludeLeapDay()
    {
        var upstream = new StubSub2ApiClient
        {
            Keys = [Key(101, "Leap Year Key")],
        };
        await using var factory = CreateFactory(upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        await ConfigureConnectionAsync(client);

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2024-03-01" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = Assert.IsType<ReportDetailResponse>(
            await response.Content.ReadFromJsonAsync<ReportDetailResponse>(JsonOptions));

        Assert.Equal(new DateOnly(2024, 2, 1), report.ThirtyDayWindow.StartDate);
        Assert.Equal(new DateOnly(2024, 2, 24), report.SevenDayWindow.StartDate);
        Assert.Equal("30", report.ThirtyDayTotal.TotalRequests);
        Assert.Equal("7", report.SevenDayTotal.TotalRequests);
        Assert.Contains(upstream.Calls, call =>
            call.StartDate <= new DateOnly(2024, 2, 29)
            && call.EndDate >= new DateOnly(2024, 2, 29));
    }

    [Fact]
    public async Task DryRunBoundsConcurrentUpstreamRequests()
    {
        var upstream = new StubSub2ApiClient
        {
            Keys = Enumerable.Range(1, 6).Select(index => Key(100 + index, $"Key {index}")).ToArray(),
            Delay = TimeSpan.FromMilliseconds(25),
        };
        await using var factory = CreateFactory(upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        await ConfigureConnectionAsync(client);
        var settings = await client.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/system/settings");
        Assert.NotNull(settings);
        using (var settingsResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/system/settings",
            new
            {
                settings.Timezone,
                settings.ReleaseChannel,
                settings.LogLevel,
                reportConcurrency = 2,
                settings.ReportRetentionMonths,
                settings.BackupRetentionCount,
                settings.Revision,
            }))
        {
            Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        }

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate = "2026-08-25" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(12, upstream.Calls.Count);
        Assert.Equal(2, upstream.MaxConcurrentCalls);
    }

    private static ApiWebApplicationFactory CreateFactory(StubSub2ApiClient upstream) => new(
        databasePath: null,
        deleteDatabaseOnDispose: true,
        configureTestServices: services =>
        {
            services.RemoveAll<ISub2ApiClient>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<ISub2ApiClient>(upstream);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        });

    private static async Task ConfigureConnectionAsync(HttpClient client)
    {
        await StepUpAsync(client);
        using var saveResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/connection",
            new
            {
                baseUrl = "https://sub2api.example.com",
                adminApiKey = AdminApiKey,
                clearAdminApiKey = false,
                codexGroupId = "7",
                revision = 0,
            });
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
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

    private static Sub2ApiExternalKey Key(long id, string name) => new(
        id,
        name,
        "active",
        7,
        null);

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

    private static async Task StepUpAsync(HttpClient client)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/step-up",
            new { password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private sealed class StubSub2ApiClient : ISub2ApiClient
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;

        public Sub2ApiExternalKey[] Keys { get; set; } = [];

        public Func<UsageCall, bool>? FailurePredicate { get; set; }

        public Sub2ApiClientException? Failure { get; set; }

        public TimeSpan Delay { get; init; }

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public ConcurrentQueue<UsageCall> Calls { get; } = new();

        public Task<Sub2ApiConnectionProbe> TestAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Sub2ApiConnectionProbe(Keys.Length));

        public Task<IReadOnlyList<Sub2ApiExternalUser>> GetUsersAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) => Failure is null
            ? Task.FromResult<IReadOnlyList<Sub2ApiExternalUser>>(
                [new Sub2ApiExternalUser(42, "user@example.com", "synthetic-user", "active")])
            : Task.FromException<IReadOnlyList<Sub2ApiExternalUser>>(Failure);

        public Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
            Sub2ApiConnectionCredentials connection,
            long externalUserId,
            CancellationToken cancellationToken) => Failure is null
            ? Task.FromResult<IReadOnlyList<Sub2ApiExternalKey>>(Keys)
            : Task.FromException<IReadOnlyList<Sub2ApiExternalKey>>(Failure);

        public async Task<Sub2ApiUsageStats> GetUsageStatsAsync(
            Sub2ApiConnectionCredentials connection,
            long externalUserId,
            long externalApiKeyId,
            DateOnly startDate,
            DateOnly endDate,
            string timezone,
            CancellationToken cancellationToken)
        {
            var call = new UsageCall(externalApiKeyId, startDate, endDate, timezone);
            Calls.Enqueue(call);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(active);
            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken);
                }

                if (FailurePredicate?.Invoke(call) == true)
                {
                    throw new Sub2ApiClientException(
                        Sub2ApiFailureKind.Unavailable,
                        "synthetic report collection failure");
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
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maxConcurrentCalls);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maxConcurrentCalls, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed record UsageCall(
        long ExternalApiKeyId,
        DateOnly StartDate,
        DateOnly EndDate,
        string Timezone);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
