using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class M5ChannelDeliveryTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private const string SyntheticWebhookUrl =
        "https://oapi.dingtalk.com/robot/send?access_token=synthetic-access-token";
    private const string SyntheticSignSecret = "synthetic-sign-secret-1";
    private const string ExpectedXlsxFileName = "sub2api-report-2026-08-25.xlsx";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly string[] RecipientAddresses = ["recipient@example.com"];
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChannelLifecycleStoresCiphertextAndMasksSecrets()
    {
        await using var factory = CreateFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var created = await CreateDingTalkChannelAsync(client);
        var listed = Assert.IsType<List<ChannelResponse>>(
            await client.GetFromJsonAsync<List<ChannelResponse>>("/api/v1/channels", JsonOptions));

        Assert.Equal(created.Id, Assert.Single(listed).Id);
        Assert.Equal("合成钉钉渠道", created.Name);
        Assert.True(created.Webhook?.HasWebhook);
        Assert.Equal("****oken", created.Webhook?.WebhookMask);
        Assert.Equal("****et-1", created.Webhook?.SignSecretMask);
        var serialized = JsonSerializer.Serialize(created, JsonOptions);
        Assert.DoesNotContain(SyntheticWebhookUrl, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticSignSecret, serialized, StringComparison.Ordinal);

        var updated = await UpdateChannelNameAsync(client, created.Id, created.Revision);
        Assert.Equal(created.Revision + 1, updated.Revision);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var stored = Assert.Single(dbContext.NotificationChannels);
        Assert.NotEqual(SyntheticWebhookUrl, stored.WebhookCiphertext);
        Assert.NotEqual(SyntheticSignSecret, stored.SignSecretCiphertext);
        Assert.Equal("oken", stored.WebhookSuffix);
        Assert.Equal("et-1", stored.SignSecretSuffix);
        Assert.Contains(dbContext.AuditEvents, audit => audit is { Action: "channels.create" });
        Assert.Contains(dbContext.AuditEvents, audit => audit is { Action: "channels.update" });
    }

    [Fact]
    public async Task DeleteChannelIsRejectedOnceDeliveriesExist()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        await using var factory = CreateFactory(dingTalkSender);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var channel = await CreateDingTalkChannelAsync(client);
        var report = await GenerateCompleteReportAsync(client);

        var deliver = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries",
            new { channelIds = new[] { channel.Id }, confirmPartial = false });
        Assert.Equal(HttpStatusCode.Created, deliver.StatusCode);
        var run = Assert.IsType<DeliveryRunResponse>(
            await deliver.Content.ReadFromJsonAsync<DeliveryRunResponse>(JsonOptions));
        Assert.Equal(ReportRunStatus.Succeeded, run.Status);

        using var deleteResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Delete,
            $"/api/v1/channels/{channel.Id:D}",
            null);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeliveryIsolatesChannelFailuresAndRetryOnlyResendsFailures()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        var emailSender = new StubReportSender(NotificationChannelType.Email);
        await using var factory = CreateFactory(dingTalkSender, emailSender);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var dingTalk = await CreateDingTalkChannelAsync(client);
        var email = await CreateEmailChannelAsync(client);
        var report = await GenerateCompleteReportAsync(client);

        dingTalkSender.OutcomeFor = _ => ChannelSendOutcome.Ok;
        emailSender.OutcomeFor = _ =>
            ChannelSendOutcome.Fail("smtp_auth_failed", "SMTP 认证失败。");

        using var deliver = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries",
            new { channelIds = new[] { dingTalk.Id, email.Id }, confirmPartial = false });
        Assert.Equal(HttpStatusCode.Created, deliver.StatusCode);
        var run = Assert.IsType<DeliveryRunResponse>(
            await deliver.Content.ReadFromJsonAsync<DeliveryRunResponse>(JsonOptions));
        Assert.Equal(ReportRunStatus.PartialFailed, run.Status);
        Assert.Equal(2, run.Deliveries.Count);
        var dingTalkDelivery = run.Deliveries.Single(item => item.ChannelId == dingTalk.Id);
        var emailDelivery = run.Deliveries.Single(item => item.ChannelId == email.Id);
        Assert.Equal(DeliveryStatus.Succeeded, dingTalkDelivery.Status);
        Assert.Equal(DeliveryStatus.Failed, emailDelivery.Status);
        Assert.Equal("smtp_auth_failed", emailDelivery.ErrorCode);

        Assert.Equal(1, dingTalkSender.SendCount);
        Assert.Equal(1, emailSender.SendCount);

        var listedRuns = Assert.IsType<List<DeliveryRunResponse>>(
            await client.GetFromJsonAsync<List<DeliveryRunResponse>>(
                $"/api/v1/reports/{report.ReportId:D}/deliveries",
                JsonOptions));
        Assert.Equal(run.Id, Assert.Single(listedRuns).Id);

        emailSender.OutcomeFor = _ => ChannelSendOutcome.Ok;
        using var retry = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries/{run.Id:D}/retry",
            null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retried = Assert.IsType<DeliveryRunResponse>(
            await retry.Content.ReadFromJsonAsync<DeliveryRunResponse>(JsonOptions));
        Assert.Equal(ReportRunStatus.Succeeded, retried.Status);
        var retriedDingTalk = retried.Deliveries.Single(item => item.ChannelId == dingTalk.Id);
        var retriedEmail = retried.Deliveries.Single(item => item.ChannelId == email.Id);
        Assert.Equal(DeliveryStatus.Succeeded, retriedDingTalk.Status);
        Assert.Equal(DeliveryStatus.Succeeded, retriedEmail.Status);
        Assert.Equal(1, retriedDingTalk.Attempts);
        Assert.Equal(2, retriedEmail.Attempts);

        Assert.Equal(1, dingTalkSender.SendCount);
        Assert.Equal(2, emailSender.SendCount);

        using var secondRetry = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries/{run.Id:D}/retry",
            null);
        Assert.Equal(HttpStatusCode.Conflict, secondRetry.StatusCode);
    }

    [Fact]
    public async Task PartialReportRequiresExplicitConfirmation()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        var upstream = new StubSub2ApiClient
        {
            Keys = [new Sub2ApiExternalKey(101, "Synthetic Key", "active", 7, null)],
            FailurePredicate = _ => true,
        };
        await using var factory = CreateFactory(dingTalkSender, upstream: upstream);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var channel = await CreateDingTalkChannelAsync(client);
        await ConfigureAndSynchronizeAsync(client);
        var report = await GenerateReportAsync(client, "2026-08-25");
        Assert.Equal(ReportStatus.Partial, report.Status);

        using var rejected = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries",
            new { channelIds = new[] { channel.Id }, confirmPartial = false });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        dingTalkSender.OutcomeFor = _ => ChannelSendOutcome.Ok;
        using var accepted = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries",
            new { channelIds = new[] { channel.Id }, confirmPartial = true });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task EmailChannelRejectsPasswordWithoutUsername()
    {
        await using var factory = CreateFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

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
                    username = (string?)null,
                    fromAddress = "reports@example.com",
                    fromName = "Sub2API Report",
                    toAddresses = RecipientAddresses,
                    ccAddresses = Array.Empty<string>(),
                },
                smtpPassword = "synthetic-password-1",
                webhookUrl = (string?)null,
                signSecret = (string?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task DingTalkDeliveryCreatesLimitedRevocableXlsxDownload()
    {
        var dingTalkSender = new StubReportSender(NotificationChannelType.DingTalk);
        await using var factory = CreateFactory(dingTalkSender);
        await InitializeAsync(factory);
        await ConfigureReportDownloadSettingsAsync(factory, maxDownloads: 2);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        var channel = await CreateDingTalkChannelAsync(client);
        var report = await GenerateCompleteReportAsync(client);
        using var deliver = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/deliveries",
            new { channelIds = new[] { channel.Id }, confirmPartial = false });
        Assert.Equal(HttpStatusCode.Created, deliver.StatusCode);
        var run = Assert.IsType<DeliveryRunResponse>(
            await deliver.Content.ReadFromJsonAsync<DeliveryRunResponse>(JsonOptions));
        var delivery = Assert.Single(run.Deliveries);
        var grant = Assert.IsType<ReportDownloadGrantResponse>(delivery.DownloadGrant);
        Assert.Equal(2, grant.MaxDownloads);
        Assert.NotNull(grant.ExpiresAt);

        var downloadUrl = Assert.IsType<string>(dingTalkSender.LastReportDownloadUrl);
        var token = QueryHelpers.ParseQuery(new Uri(downloadUrl).Query)["token"].ToString();
        Assert.NotEmpty(token);
        for (var index = 0; index < 2; index++)
        {
            using var download = await client.GetAsync(
                $"/api/v1/report-downloads/xlsx?token={Uri.EscapeDataString(token)}");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Contains("no-store", download.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
            Assert.Equal("no-referrer", Assert.Single(download.Headers.GetValues("Referrer-Policy")));
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ExpectedXlsxFileName, download.Content.Headers.ContentDisposition?.FileName);
            var content = await download.Content.ReadAsByteArrayAsync();
            ReportXlsxAssert.AssertZipXlsxBytes(content);
            using var workbook = ReportXlsxAssert.Open(content);
            ReportXlsxAssert.AssertOverviewTitle(workbook);
        }

        using (var exhausted = await client.GetAsync(
            $"/api/v1/report-downloads/xlsx?token={Uri.EscapeDataString(token)}"))
        {
            Assert.Equal(HttpStatusCode.Gone, exhausted.StatusCode);
        }

        using var revoke = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            $"/api/v1/reports/{report.ReportId:D}/download-grants/{grant.Id:D}/revoke",
            null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var runs = Assert.IsType<List<DeliveryRunResponse>>(
            await client.GetFromJsonAsync<List<DeliveryRunResponse>>(
                $"/api/v1/reports/{report.ReportId:D}/deliveries",
                JsonOptions));
        var refreshedGrant = Assert.IsType<ReportDownloadGrantResponse>(
            Assert.Single(Assert.Single(runs).Deliveries).DownloadGrant);
        Assert.Equal(2, refreshedGrant.DownloadCount);
        Assert.NotNull(refreshedGrant.RevokedAt);
    }



    private static async Task ConfigureReportDownloadSettingsAsync(
        ApiWebApplicationFactory factory,
        int? maxDownloads)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        var current = await settingsService.GetAsync(CancellationToken.None);
        _ = await settingsService.UpdateAsync(
            new UpdateSystemSettingsCommand(
                current.Timezone,
                current.LogLevel,
                current.ReportConcurrency,
                current.ReportRetentionMonths,
                current.BackupRetentionCount,
                current.Revision,
                "https://reports.example.com",
                24,
                maxDownloads),
            CancellationToken.None);
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

    private static async Task<ChannelResponse> UpdateChannelNameAsync(
        HttpClient client,
        Guid channelId,
        long revision)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/channels/{channelId:D}",
            new
            {
                name = "合成钉钉渠道 2",
                enabled = true,
                email = (object?)null,
                removeStoredPassword = false,
                newSmtpPassword = (string?)null,
                webhookUrl = (string?)null,
                signSecret = (string?)null,
                revision,
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<ChannelResponse>(
            await response.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions));
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

    private static async Task<ReportDetailResponse> GenerateCompleteReportAsync(HttpClient client)
    {
        await ConfigureAndSynchronizeAsync(client);
        return await GenerateReportAsync(client, "2026-08-25");
    }

    private static async Task<ReportDetailResponse> GenerateReportAsync(
        HttpClient client,
        string cutoffDate)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reports/dry-run",
            new { cutoffDate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<ReportDetailResponse>(
            await response.Content.ReadFromJsonAsync<ReportDetailResponse>(JsonOptions));
    }

    private static ApiWebApplicationFactory CreateFactory(
        StubReportSender? dingTalkSender = null,
        StubReportSender? emailSender = null,
        StubSub2ApiClient? upstream = null)
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
                services.AddSingleton<IReportSender>(dingTalkSender
                    ?? new StubReportSender(NotificationChannelType.DingTalk));
                services.AddSingleton<IReportSender>(emailSender
                    ?? new StubReportSender(NotificationChannelType.Email));
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
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

    private sealed class StubReportSender(NotificationChannelType type) : IReportSender
    {
        private int _sendCount;

        public NotificationChannelType ChannelType => type;

        public Func<string, ChannelSendOutcome>? OutcomeFor { get; set; }

        public int SendCount => Volatile.Read(ref _sendCount);

        public string? LastReportDownloadUrl { get; private set; }

        public IReadOnlyList<OutboundPart> Render(
            ReportDocument report,
            ChannelDeliveryContext context)
        {
            LastReportDownloadUrl = context.ReportDownloadUrl;
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

    private sealed class StubSub2ApiClient : ISub2ApiClient
    {
        public Sub2ApiExternalKey[] Keys { get; set; } = [];

        public Func<Sub2ApiUsageStatsCall, bool>? FailurePredicate { get; set; }

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

        public Task<Sub2ApiUsageStats> GetUsageStatsAsync(
            Sub2ApiConnectionCredentials connection,
            long externalUserId,
            long externalApiKeyId,
            DateOnly startDate,
            DateOnly endDate,
            string timezone,
            CancellationToken cancellationToken)
        {
            if (FailurePredicate?.Invoke(new Sub2ApiUsageStatsCall(
                    externalApiKeyId,
                    startDate,
                    endDate,
                    timezone)) == true)
            {
                throw new Sub2ApiClientException(
                    Sub2ApiFailureKind.Unavailable,
                    "synthetic report collection failure");
            }

            var days = endDate.DayNumber - startDate.DayNumber + 1;
            return Task.FromResult(new Sub2ApiUsageStats(
                days,
                days * 100L,
                days * 50L,
                days * 25L,
                days * 10L,
                days * 15L,
                days * 175L,
                days * 0.2m,
                days * 0.1m,
                125.5m));
        }
    }

    private sealed record Sub2ApiUsageStatsCall(
        long ExternalApiKeyId,
        DateOnly StartDate,
        DateOnly EndDate,
        string Timezone);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
