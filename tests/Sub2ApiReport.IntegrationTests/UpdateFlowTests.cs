using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Api.Updates;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.IntegrationTests;

public sealed class UpdateFlowTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";

    [Fact]
    public async Task InternalMaintenanceRequiresTokenAndGatesBusinessTraffic()
    {
        var token = new string('a', 64);
        var tokenFile = Path.Combine(Path.GetTempPath(), $"updater-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tokenFile, token);
        try
        {
            await using var factory = CreateFactory(tokenFile);
            using var client = CreateClient(factory);

            using var unauthorized = await client.GetAsync("/internal/v1/update-handshake");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var initial = await client.GetFromJsonAsync<AppUpdateHandshakeResponse>("/internal/v1/update-handshake");
            Assert.NotNull(initial);
            Assert.False(initial.MaintenanceMode);

            var operationId = Guid.NewGuid().ToString("N");
            using var enter = await client.PostAsJsonAsync(
                "/internal/v1/maintenance/enter",
                new AppMaintenanceRequest(operationId));
            Assert.Equal(HttpStatusCode.NoContent, enter.StatusCode);

            var active = await client.GetFromJsonAsync<AppUpdateHandshakeResponse>("/internal/v1/update-handshake");
            Assert.NotNull(active);
            Assert.True(active.MaintenanceMode);
            Assert.Equal(operationId, active.MaintenanceOperationId);

            using var business = await client.GetAsync("/api/v1/system/version");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, business.StatusCode);
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            using var wrongComplete = await client.PostAsJsonAsync(
                "/internal/v1/maintenance/complete",
                new AppMaintenanceRequest(Guid.NewGuid().ToString("N")));
            Assert.Equal(HttpStatusCode.Conflict, wrongComplete.StatusCode);

            using var complete = await client.PostAsJsonAsync(
                "/internal/v1/maintenance/complete",
                new AppMaintenanceRequest(operationId));
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
            using var restored = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public async Task CandidateMaintenanceKeepsTechnicalReadinessGreen()
    {
        var token = new string('b', 64);
        var operationId = Guid.NewGuid().ToString("N");
        var tokenFile = Path.Combine(Path.GetTempPath(), $"updater-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tokenFile, token);
        try
        {
            await using var factory = CreateFactory(
                tokenFile,
                new Dictionary<string, string?>
                {
                    ["Update:MaintenanceOperationId"] = operationId,
                });
            using var client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var handshake = await client.GetFromJsonAsync<AppUpdateHandshakeResponse>("/internal/v1/update-handshake");
            Assert.NotNull(handshake);
            Assert.True(handshake.MaintenanceMode);
            Assert.Equal("candidate_verification", handshake.MaintenanceState);
            Assert.Equal(operationId, handshake.MaintenanceOperationId);

            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            using var business = await client.GetAsync("/api/v1/system/version");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, business.StatusCode);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public async Task AdminInstallRequiresAuthenticationAntiforgeryAndRecentStepUp()
    {
        var tokenFile = Path.Combine(Path.GetTempPath(), $"updater-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tokenFile, new string('c', 64));
        var updaterClient = new FakeUpdaterClient();
        try
        {
            await using var factory = CreateFactory(tokenFile, configureServices: services =>
            {
                services.RemoveAll<IUpdaterClient>();
                services.AddSingleton<IUpdaterClient>(updaterClient);
            });
            using var client = CreateClient(factory);

            using var anonymousStatus = await client.GetAsync("/api/v1/updates/status");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus.StatusCode);

            await InitializeAsync(factory);
            await LoginAsync(client);
            using var status = await client.GetAsync("/api/v1/updates/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);

            using var noStepUp = await CreateJsonRequestAsync(
                client,
                HttpMethod.Post,
                "/api/v1/updates/install",
                new { confirm = true, targetVersion = "1.0.0" });
            using var noStepUpResponse = await client.SendAsync(noStepUp);
            Assert.Equal(HttpStatusCode.Forbidden, noStepUpResponse.StatusCode);

            using var stepUp = await CreateJsonRequestAsync(
                client,
                HttpMethod.Post,
                "/api/v1/auth/step-up",
                new { password = Password });
            using var stepUpResponse = await client.SendAsync(stepUp);
            Assert.Equal(HttpStatusCode.OK, stepUpResponse.StatusCode);

            using var install = await CreateJsonRequestAsync(
                client,
                HttpMethod.Post,
                "/api/v1/updates/install",
                new { confirm = true, targetVersion = "1.0.0" });
            using var installResponse = await client.SendAsync(install);
            Assert.Equal(HttpStatusCode.Accepted, installResponse.StatusCode);
            Assert.Equal("1.0.0", updaterClient.LastTargetVersion);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    private static ApiWebApplicationFactory CreateFactory(
        string tokenFile,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var values = new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>())
        {
            ["Updater:TokenFile"] = tokenFile,
            ["Updater:BaseUrl"] = "http://updater.invalid",
        };
        return new ApiWebApplicationFactory(
            databasePath: null,
            deleteDatabaseOnDispose: true,
            configureTestServices: configureServices,
            settings: values);
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
        using var request = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = issue.Code, username = Username, password = Password });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var request = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username = Username, password = Password });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpRequestMessage> CreateJsonRequestAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/security/antiforgery");
        Assert.NotNull(token);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", token.Token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class FakeUpdaterClient : IUpdaterClient
    {
        public string? LastTargetVersion { get; private set; }

        public Task<UpdaterStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UpdaterStatusResponse("0.9.0", true, "update_available", null, "1.0.0"));

        public Task<UpdateCheckResponse> CheckAsync(string currentVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new UpdateCheckResponse(true, currentVersion, "1.0.0", DateTimeOffset.UtcNow, false));

        public Task<UpdatePlanResponse> GetPlanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UpdatePlanResponse("0.9.0", "1.0.0", true, false, []));

        public Task<InstallAcceptedResponse> InstallAsync(
            string currentVersion,
            string? targetVersion,
            CancellationToken cancellationToken)
        {
            LastTargetVersion = targetVersion;
            return Task.FromResult(new InstallAcceptedResponse(Guid.NewGuid().ToString("N"), "queued"));
        }

        public Task<InstallOperationResponse?> GetOperationAsync(
            string operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<InstallOperationResponse?>(null);
    }
}
