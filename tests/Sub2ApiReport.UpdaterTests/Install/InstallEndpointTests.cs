using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.UpdaterTests.Install;

public sealed class InstallEndpointTests : IDisposable
{
    private readonly List<UpdaterTestServer> _servers = [];

    [Fact]
    public async Task InstallRejectedWhenInstallationDisabled()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: false);
        using var client = CreateAuthorizedClient(server);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("未启用", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedWithoutPriorCheck()
    {
        var server = CreateServer(installationEnabled: true);
        using var client = CreateAuthorizedClient(server);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("更新检查", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedForManualUpgradeRequired()
    {
        var version = TestReleases.DefaultVersion;
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder()
            .WithVersion(version)
            .WithManualUpgradeRequired(true)
            .WithUpgradeMessage("该版本要求手工完整 bundle 升级。")
            .Build();
        var server = await CreateServerWithReleaseAsync(
            installationEnabled: true, key, publicPem, manifest);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("手工", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedWhenSourceVersionWasNotVerified()
    {
        var version = TestReleases.DefaultVersion;
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder()
            .WithVersion(version)
            .WithManualUpgradeRequired(false)
            .WithOnlineInstallSupported(true)
            .WithOnlineUpgradeFrom("0.8.0")
            .Build();
        var server = await CreateServerWithReleaseAsync(
            installationEnabled: true, key, publicPem, manifest);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("未验证", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedWhenUpdaterVersionIsBelowMinimum()
    {
        var version = TestReleases.DefaultVersion;
        var (key, publicPem) = TestKeys.CreateSigningKey();
        using var rsa = key;
        var manifest = new ReleaseManifestBuilder()
            .WithVersion(version)
            .WithMinimumUpdaterVersion(version)
            .WithOnlineInstallSupported(true)
            .WithManualUpgradeRequired(false)
            .Build();
        var server = await CreateServerWithReleaseAsync(
            installationEnabled: true, key, publicPem, manifest);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("Updater", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedForVersionMismatch()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: true);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}","targetVersion":"9.9.9"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("不一致", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InstallRejectedWhenCurrentVersionNotLowerThanTarget()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: true);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.DefaultVersion}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InstallRejectsInvalidCurrentVersion()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: true);
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var response = await client.PostAsync(
            "/internal/v1/install",
            JsonBody("""{"currentVersion":"not-a-semver"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InstallAcceptedReturns202AndOperationCompletesSucceeded()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: true);
        // 候选 App 替换后握手返回目标版本。
        ((FakeMaintenanceClient)server.Factory.Services.GetRequiredService<IAppMaintenanceClient>())
            .VersionAfterComplete = TestReleases.DefaultVersion;
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var accepted = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var install = await accepted.Content.ReadFromJsonAsync<InstallAcceptedResponse>();
        Assert.NotNull(install);
        Assert.Equal(InstallOperationStates.Queued, install.State);

        // 轮询至终态（后台队列 + 事务替身）。
        var operation = await PollUntilTerminalAsync(client, install.OperationId);
        Assert.Equal(InstallOperationStates.Succeeded, operation.State);
        Assert.NotEmpty(operation.Stages);

        using var statusResponse = await client.GetAsync("/internal/v1/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<UpdaterStatusResponse>();
        Assert.NotNull(status);
        Assert.True(status.InstallationEnabled);
        Assert.Equal(install.OperationId, status.LastOperationId);
        Assert.Equal(InstallOperationStates.Succeeded, status.LastOperationState);
    }

    [Fact]
    public async Task SecondInstallWhileBusyReturnsConflict()
    {
        var blocker = new TaskCompletionSource();
        var server = await CreateServerWithReleaseAsync(
            installationEnabled: true,
            configure: services =>
            {
                services.RemoveAll<IInstallTransaction>();
                services.AddSingleton<IInstallTransaction>(new BlockingTransaction(blockUntilReleased: blocker.Task));
            });
        using var client = CreateAuthorizedClient(server);
        await CheckAsync(client);

        using var first = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<InstallAcceptedResponse>();

        // 等待第一个操作真正开始执行（事务被调用）。
        var store = server.Factory.Services.GetRequiredService<Sub2ApiReport.Updater.State.UpdateStateStore>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var operation = await store.LoadOperationAsync(firstResult!.OperationId, CancellationToken.None);
            if (operation is not null && operation.State != InstallOperationStates.Queued)
            {
                break;
            }

            await Task.Delay(50);
        }

        using var second = await client.PostAsync(
            "/internal/v1/install",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        blocker.SetResult();
    }

    [Fact]
    public async Task InstallOperationEndpointReturns404ForUnknownId()
    {
        var server = await CreateServerWithReleaseAsync(installationEnabled: true);
        using var client = CreateAuthorizedClient(server);

        using var response = await client.GetAsync($"/internal/v1/install/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    private static async Task<InstallOperationResponse> PollUntilTerminalAsync(HttpClient client, string operationId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"/internal/v1/install/{operationId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var operation = await response.Content.ReadFromJsonAsync<InstallOperationResponse>();
                if (operation is not null && InstallOperationStates.IsTerminal(operation.State))
                {
                    return operation;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("安装操作未在超时时间内到达终态。");
    }

    private static async Task<UpdateCheckResponse> CheckAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            "/internal/v1/check",
            JsonBody($$"""{"currentVersion":"{{TestReleases.CurrentAppVersion}}"}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>();
        Assert.NotNull(check);
        return check;
    }

    private UpdaterTestServer CreateServer(bool installationEnabled = false) =>
        CreateServerAsync(installationEnabled).GetAwaiter().GetResult();

    private async Task<UpdaterTestServer> CreateServerAsync(bool installationEnabled) =>
        await CreateServerWithReleaseAsync(installationEnabled);

    /// <summary>构造带已签名 Release（可在线安装）与下载内容的服务器并默认执行检查所需的资产。</summary>
    private async Task<UpdaterTestServer> CreateServerWithReleaseAsync(
        bool installationEnabled,
        RSA? signingKey = null,
        string? publicPem = null,
        ReleaseManifest? manifest = null,
        Action<IServiceCollection>? configure = null)
    {
        RSA? ownedKey = null;
        if (signingKey is null || publicPem is null)
        {
            var (key, pem) = TestKeys.CreateSigningKey();
            ownedKey = key;
            signingKey = key;
            publicPem = pem;
        }

        var version = manifest?.Version ?? TestReleases.DefaultVersion;
        var effectiveManifest = manifest
            ?? new ReleaseManifestBuilder()
                .WithVersion(version)
                .WithOnlineInstallSupported(true)
                .WithManualUpgradeRequired(false)
                .WithAppArchiveSha256(TestSnapshots.ComputeSha256(EndpointArchiveBytes))
                .WithAppSize(EndpointArchiveBytes.Length)
                .Build();
        var manifestBytes = TestReleases.ToJson(effectiveManifest);
        var signature = TestKeys.Sign(signingKey, manifestBytes);

        var downloader = new StubDownloader(new Dictionary<string, byte[]>
        {
            [TestReleases.ManifestAssetUrl(version)] = manifestBytes,
            [TestReleases.ManifestSignatureUrl(version)] = signature,
            [TestReleases.AppArchiveUrl(version)] = EndpointArchiveBytes,
        });

        var server = UpdaterTestServerFactory.Create(
            publicPem,
            new StubGitHubReleaseClient(TestReleases.CreateRelease(version, manifestBytes, signature)),
            downloader,
            withToken: true,
            configureServices: configure,
            installationEnabled: installationEnabled);
        _servers.Add(server);
        if (ownedKey is not null)
        {
            // 生命周期由 RSA 自身管理；测试期间保持引用即可。
            GC.KeepAlive(ownedKey);
        }

        await Task.CompletedTask;
        return server;
    }

    private static readonly byte[] EndpointArchiveBytes = [0x1f, 0x8b, 0x08, 0x00, 0x01];

    private static HttpClient CreateAuthorizedClient(UpdaterTestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", server.Token);
        return client;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>阻塞事务：记录调用并等待释放信号（用于队列忙碌测试）。</summary>
    private sealed class BlockingTransaction : IInstallTransaction
    {
        public BlockingTransaction(Task blockUntilReleased) => BlockUntilReleased = blockUntilReleased;

        public Task BlockUntilReleased { get; }

        public async Task<InstallOperationRecord> ExecuteAsync(
            InstallOperationRecord operation,
            CancellationToken cancellationToken)
        {
            await BlockUntilReleased.WaitAsync(cancellationToken);
            return operation with
            {
                State = InstallOperationStates.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }
    }
}
