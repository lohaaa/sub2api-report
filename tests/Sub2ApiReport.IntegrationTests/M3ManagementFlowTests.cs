using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class M3ManagementFlowTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private const string AdminApiKey = "synthetic-sub2api-admin-key-1234";

    [Fact]
    public async Task M3EndpointsRequireAuthentication()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);

        using var connectionResponse = await client.GetAsync("/api/v1/sub2api/connection");
        using var peopleResponse = await client.GetAsync("/api/v1/people");
        using var keysResponse = await client.GetAsync("/api/v1/sub2api/keys");

        Assert.Equal(HttpStatusCode.Unauthorized, connectionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, peopleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, keysResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanConfigureSynchronizeAndAssignKeysWithoutPersistingSecrets()
    {
        var upstream = new StubSub2ApiClient
        {
            Keys =
            [
                Key(101, "Alpha", "active"),
                Key(102, "Beta", "active"),
            ],
        };
        await using var factory = new ApiWebApplicationFactory(
            databasePath: null,
            deleteDatabaseOnDispose: true,
            configureTestServices: services =>
            {
                services.RemoveAll<ISub2ApiClient>();
                services.AddSingleton<ISub2ApiClient>(upstream);
            });
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var deniedSave = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/connection",
            ConnectionRequest(AdminApiKey));
        Assert.Equal(HttpStatusCode.Forbidden, deniedSave.StatusCode);

        await StepUpAsync(client);
        using var saveResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/connection",
            ConnectionRequest(AdminApiKey));
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var saved = await saveResponse.Content.ReadFromJsonAsync<Sub2ApiConnectionResponse>();
        Assert.NotNull(saved);
        Assert.True(saved.HasAdminApiKey);
        Assert.Equal("****1234", saved.AdminApiKeyMask);
        using var staleSaveResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/sub2api/connection",
            ConnectionRequest(AdminApiKey));
        Assert.Equal(HttpStatusCode.Conflict, staleSaveResponse.StatusCode);

        var publicConnectionJson = await client.GetStringAsync("/api/v1/sub2api/connection");
        Assert.DoesNotContain(AdminApiKey, publicConnectionJson, StringComparison.Ordinal);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var stored = Assert.Single(dbContext.Sub2ApiConnections);
            Assert.NotEqual(AdminApiKey, stored.AdminApiKeyCiphertext);
            Assert.DoesNotContain(AdminApiKey, stored.AdminApiKeyCiphertext, StringComparison.Ordinal);
        }

        using var testResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/sub2api/connection/test",
            null);
        Assert.Equal(HttpStatusCode.OK, testResponse.StatusCode);
        var tested = Assert.IsType<Sub2ApiConnectionTestResponse>(
            await testResponse.Content.ReadFromJsonAsync<Sub2ApiConnectionTestResponse>());
        Assert.True(tested.Succeeded);
        Assert.Equal(2, tested.AvailableKeyCount);

        using var firstSyncResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/sub2api/keys/sync",
            null);
        Assert.Equal(HttpStatusCode.OK, firstSyncResponse.StatusCode);
        var firstSync = await firstSyncResponse.Content.ReadFromJsonAsync<KeySynchronizationResponse>();
        Assert.Equal(2, firstSync?.Added);

        upstream.Failure = new Sub2ApiClientException(
            Sub2ApiFailureKind.Unavailable,
            "synthetic upstream failure");
        using var failedSyncResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/sub2api/keys/sync",
            null);
        Assert.Equal(HttpStatusCode.BadGateway, failedSyncResponse.StatusCode);
        var unchangedInventory = await client.GetFromJsonAsync<ApiKeyInventoryPageResponse>(
            "/api/v1/sub2api/keys?page=1&pageSize=50");
        Assert.Equal(2, unchangedInventory?.Total);
        upstream.Failure = null;

        var firstPerson = await CreatePersonAsync(client, "person-a", "合成人员 A");
        var secondPerson = await CreatePersonAsync(client, "person-b", "合成人员 B");
        var inventory = await client.GetFromJsonAsync<ApiKeyInventoryPageResponse>(
            "/api/v1/sub2api/keys?page=1&pageSize=50");
        Assert.NotNull(inventory);
        Assert.Equal(2, inventory.Diagnostics.UnmappedKeys);
        var firstKey = inventory.Items.Single(item => item.ExternalId == "101");

        using var assignmentResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/people/{firstPerson.Id}/assignments",
            new
            {
                externalApiKeyId = firstKey.Id,
                validFrom = "2026-01-01",
                validTo = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);

        using var overlappingResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/people/{secondPerson.Id}/assignments",
            new
            {
                externalApiKeyId = firstKey.Id,
                validFrom = "2026-08-01",
                validTo = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Conflict, overlappingResponse.StatusCode);

        inventory = await client.GetFromJsonAsync<ApiKeyInventoryPageResponse>(
            "/api/v1/sub2api/keys?page=1&pageSize=50");
        Assert.NotNull(inventory);
        Assert.Equal(1, inventory.Diagnostics.UnmappedKeys);
        Assert.Equal(0, inventory.Diagnostics.OverlappingAssignments);

        upstream.Keys = [Key(101, "Alpha renamed", "inactive")];
        using var secondSyncResponse = await SendJsonAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/sub2api/keys/sync",
            null);
        Assert.Equal(HttpStatusCode.OK, secondSyncResponse.StatusCode);
        var secondSync = await secondSyncResponse.Content.ReadFromJsonAsync<KeySynchronizationResponse>();
        Assert.Equal(1, secondSync?.Updated);
        Assert.Equal(1, secondSync?.Retired);

        inventory = await client.GetFromJsonAsync<ApiKeyInventoryPageResponse>(
            "/api/v1/sub2api/keys?page=1&pageSize=50");
        Assert.NotNull(inventory);
        Assert.Equal("Alpha renamed", inventory.Items.Single(item => item.ExternalId == "101").Name);
        Assert.NotNull(inventory.Items.Single(item => item.ExternalId == "102").RetiredAt);
        Assert.Equal(1, inventory.Diagnostics.UnmappedKeys);
        Assert.Equal(1, inventory.Diagnostics.RetiredKeys);

        await using var auditScope = factory.Services.CreateAsyncScope();
        var auditContext = auditScope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.DoesNotContain(auditContext.AuditEvents, auditEvent =>
            auditEvent.MetadataJson?.Contains(AdminApiKey, StringComparison.Ordinal) == true);
    }

    private static object ConnectionRequest(string adminApiKey) => new
    {
        baseUrl = "https://sub2api.example.com",
        adminApiKey,
        clearAdminApiKey = false,
        userId = "42",
        codexGroupId = "7",
        revision = 0,
    };

    private static Sub2ApiExternalKey Key(long id, string name, string status) => new(
        id,
        name,
        status,
        7,
        null);

    private static async Task<PersonResponse> CreatePersonAsync(
        HttpClient client,
        string code,
        string displayName)
    {
        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/people",
            new { code, displayName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<PersonResponse>(await response.Content.ReadFromJsonAsync<PersonResponse>());
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
        public IReadOnlyList<Sub2ApiExternalKey> Keys { get; set; } = [];

        public Sub2ApiClientException? Failure { get; set; }

        public Task<Sub2ApiConnectionProbe> TestAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Sub2ApiConnectionProbe(Keys.Count));

        public Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
            Sub2ApiConnectionCredentials connection,
            CancellationToken cancellationToken) => Failure is null
            ? Task.FromResult(Keys)
            : Task.FromException<IReadOnlyList<Sub2ApiExternalKey>>(Failure);

        public Task<Sub2ApiUsageStats> GetUsageStatsAsync(
            Sub2ApiConnectionCredentials connection,
            long externalApiKeyId,
            DateOnly startDate,
            DateOnly endDate,
            string timezone,
            CancellationToken cancellationToken) => Task.FromResult(new Sub2ApiUsageStats(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0));
    }
}
