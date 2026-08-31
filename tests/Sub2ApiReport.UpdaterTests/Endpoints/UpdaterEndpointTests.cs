using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests.Endpoints;

public sealed class UpdaterEndpointTests : IDisposable
{
    private readonly List<UpdaterTestServer> _servers = [];

    [Fact]
    public async Task StatusWithoutTokenReturnsUnauthorizedProblem()
    {
        var server = CreateServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/internal/v1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StatusWithWrongTokenReturnsUnauthorized()
    {
        var server = CreateServer();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('f', 64));

        using var response = await client.GetAsync("/internal/v1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingConfiguredTokenFailsClosed()
    {
        var server = CreateServer(withToken: false);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", UpdaterTestServerFactory.NewToken());

        using var response = await client.GetAsync("/internal/v1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LivenessEndpointDoesNotRequireToken()
    {
        var server = CreateServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StatusWithTokenReturnsIdleState()
    {
        var server = CreateServer();
        using var client = CreateAuthorizedClient(server);

        using var response = await client.GetAsync("/internal/v1/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<UpdaterStatusResponse>();
        Assert.NotNull(status);
        Assert.False(status.InstallationEnabled);
        Assert.Equal("idle", status.State);
        Assert.Null(status.LastCheckedAt);
        Assert.Null(status.AvailableVersion);
        Assert.NotEmpty(status.Version);
    }

    [Fact]
    public async Task CheckWithValidSignedManifestReturnsUpdateAvailableAndPersistsState()
    {
        var version = "1.2.0";
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder().WithVersion(version).Build();
        var manifestBytes = TestReleases.ToJson(manifest);
        var signature = TestKeys.Sign(rsa, manifestBytes);
        var server = CreateServer(
            publicPem,
            releaseClient: new StubGitHubReleaseClient(
                TestReleases.CreateRelease(version, manifestBytes, signature)),
            downloader: new StubDownloader(new Dictionary<string, byte[]>
            {
                [TestReleases.ManifestAssetUrl(version)] = manifestBytes,
                [TestReleases.ManifestSignatureUrl(version)] = signature,
            }));
        using var client = CreateAuthorizedClient(server);

        using var checkResponse = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));
        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        var check = await checkResponse.Content.ReadFromJsonAsync<UpdateCheckResponse>();
        Assert.NotNull(check);
        Assert.True(check.UpdateAvailable);
        Assert.Equal(TestReleases.CurrentAppVersion, check.CurrentVersion);
        Assert.Equal(version, check.AvailableVersion);
        Assert.True(check.ManualUpgradeRequired);
        Assert.Equal("请使用完整 Release bundle。", check.UpgradeMessage);
        Assert.Equal(TestReleases.PublishedAt, check.PublishedAt);

        using var statusResponse = await client.GetAsync("/internal/v1/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<UpdaterStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("update_available", status.State);
        Assert.Equal(version, status.AvailableVersion);
        Assert.NotNull(status.LastCheckedAt);

        using var planResponse = await client.GetAsync("/internal/v1/plan");
        Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
        var plan = await planResponse.Content.ReadFromJsonAsync<UpdatePlanResponse>();
        Assert.NotNull(plan);
        Assert.Equal(TestReleases.CurrentAppVersion, plan.CurrentVersion);
        Assert.Equal(version, plan.TargetVersion);
        Assert.False(plan.InstallationEnabled);
        Assert.True(plan.ManualUpgradeRequired);
        Assert.Equal("请使用完整 Release bundle。", plan.UpgradeMessage);
        Assert.Equal(7, plan.Steps.Count);
    }

    [Fact]
    public async Task CheckWithoutUpdateReturnsUpToDateState()
    {
        var version = "0.1.0";
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder().WithVersion(version).Build();
        var manifestBytes = TestReleases.ToJson(manifest);
        var signature = TestKeys.Sign(rsa, manifestBytes);
        var server = CreateServer(
            publicPem,
            releaseClient: new StubGitHubReleaseClient(
                TestReleases.CreateRelease(version, manifestBytes, signature)),
            downloader: new StubDownloader(new Dictionary<string, byte[]>
            {
                [TestReleases.ManifestAssetUrl(version)] = manifestBytes,
                [TestReleases.ManifestSignatureUrl(version)] = signature,
            }));
        using var client = CreateAuthorizedClient(server);

        using var checkResponse = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{version}}"}"""));

        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        var check = await checkResponse.Content.ReadFromJsonAsync<UpdateCheckResponse>();
        Assert.NotNull(check);
        Assert.False(check.UpdateAvailable);
        using var statusResponse = await client.GetAsync("/internal/v1/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<UpdaterStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("up_to_date", status.State);
    }

    [Fact]
    public async Task CheckWithTamperedSignatureReturns502AndPersistsFailure()
    {
        var version = "1.2.0";
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder().WithVersion(version).Build();
        var manifestBytes = TestReleases.ToJson(manifest);
        var signature = TestKeys.Sign(rsa, manifestBytes);
        var tamperedSignature = signature.ToArray();
        tamperedSignature[^1] ^= 0xFF;
        var server = CreateServer(
            publicPem,
            releaseClient: new StubGitHubReleaseClient(
                TestReleases.CreateRelease(version, manifestBytes, tamperedSignature)),
            downloader: new StubDownloader(new Dictionary<string, byte[]>
            {
                [TestReleases.ManifestAssetUrl(version)] = manifestBytes,
                [TestReleases.ManifestSignatureUrl(version)] = tamperedSignature,
            }));
        using var client = CreateAuthorizedClient(server);

        using var checkResponse = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.BadGateway, checkResponse.StatusCode);
        using var statusResponse = await client.GetAsync("/internal/v1/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<UpdaterStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("check_failed", status.State);
        Assert.Null(status.AvailableVersion);
        Assert.NotNull(status.LastCheckedAt);
    }

    [Fact]
    public async Task CheckRejectsUnknownManifestField()
    {
        var version = "1.2.0";
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder().WithVersion(version).Build();
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(manifest))!;
        json["sneakyField"] = 1;
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(json);
        var signature = TestKeys.Sign(rsa, manifestBytes);
        var server = CreateServer(
            publicPem,
            releaseClient: new StubGitHubReleaseClient(
                TestReleases.CreateRelease(version, manifestBytes, signature)),
            downloader: new StubDownloader(new Dictionary<string, byte[]>
            {
                [TestReleases.ManifestAssetUrl(version)] = manifestBytes,
                [TestReleases.ManifestSignatureUrl(version)] = signature,
            }));
        using var client = CreateAuthorizedClient(server);

        using var response = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task CheckRejectsInvalidCurrentVersion()
    {
        var server = CreateServer();
        using var client = CreateAuthorizedClient(server);

        using var response = await client.PostAsync(
            "/internal/v1/check",
            JsonBody("""{"currentVersion":"not-a-semver"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CheckRejectsUnknownRequestField()
    {
        var server = CreateServer();
        using var client = CreateAuthorizedClient(server);

        using var response = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}","sneaky":1}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlanWithoutCheckReturnsConflict()
    {
        var server = CreateServer();
        using var client = CreateAuthorizedClient(server);

        using var response = await client.GetAsync("/internal/v1/plan");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    private UpdaterTestServer CreateServer(
        string? publicKeyPem = null,
        IGitHubReleaseClient? releaseClient = null,
        IDownloader? downloader = null,
        bool withToken = true)
    {
        var server = UpdaterTestServerFactory.Create(publicKeyPem, releaseClient, downloader, withToken);
        _servers.Add(server);
        return server;
    }

    private static HttpClient CreateAuthorizedClient(UpdaterTestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", server.Token);
        return client;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}
