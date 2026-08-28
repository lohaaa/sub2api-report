using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Docker.DotNet;
using Microsoft.AspNetCore.Http.Json;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Endpoints;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Middleware;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.Services;
using Sub2ApiReport.Updater.State;

var builder = WebApplication.CreateBuilder(args);

var updateOptions = builder.Configuration
    .GetSection(UpdateOptions.SectionName)
    .Get<UpdateOptions>() ?? new UpdateOptions();
updateOptions.PublicKeyPath = Path.IsPathRooted(updateOptions.PublicKeyPath)
    ? updateOptions.PublicKeyPath
    : Path.Combine(builder.Environment.ContentRootPath, updateOptions.PublicKeyPath);
updateOptions.StatePath = Path.IsPathRooted(updateOptions.StatePath)
    ? updateOptions.StatePath
    : Path.Combine(builder.Environment.ContentRootPath, updateOptions.StatePath);

builder.Services.AddSingleton(updateOptions);
builder.Services.AddSingleton(_ => new UpdateStateStore(updateOptions.StatePath));
builder.Services.AddSingleton(_ => new GlobalOperationLock(updateOptions.StatePath));
builder.Services.AddSingleton(_ => new ReleasePublicKeyProvider(updateOptions.PublicKeyPath));
builder.Services.AddSingleton(_ => new UpdaterTokenProvider(updateOptions.TokenFile));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHttpClient<GitHubReleaseClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("sub2api-report-updater");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
});
builder.Services.AddSingleton<IGitHubReleaseClient>(services =>
    services.GetRequiredService<GitHubReleaseClient>());

var downloadClient = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    ConnectTimeout = TimeSpan.FromSeconds(15),
})
{
    Timeout = TimeSpan.FromMinutes(10),
};
builder.Services.AddSingleton<IDownloader>(_ => new RestrictedDownloader(downloadClient));

builder.Services.AddSingleton<UpdateCheckService>();
builder.Services.AddSingleton<IReleaseCacheService>(services =>
    new ReleaseCacheService(
        services.GetRequiredService<UpdateStateStore>(),
        services.GetRequiredService<ReleasePublicKeyProvider>(),
        updateOptions,
        services.GetRequiredService<TimeProvider>()));

// Docker：Updater 是唯一允许访问 Docker Engine 的组件；只通过 Docker.DotNet API 操作。
builder.Services.AddSingleton(_ =>
    new DockerClientConfiguration(new Uri(updateOptions.DockerEndpoint)).CreateClient());
builder.Services.AddSingleton<IDockerAppManager>(services =>
    new DockerAppManager(
        services.GetRequiredService<DockerClient>(),
        updateOptions));

builder.Services.AddSingleton<ISqliteBackupService>(services =>
    new SqliteBackupService(
        updateOptions,
        services.GetRequiredService<UpdateStateStore>()));

// App 维护握手与健康验证：只访问配置的 App 内部地址与固定路径。
builder.Services.AddHttpClient(
    "updater-app-maintenance",
    client =>
    {
        client.BaseAddress = new Uri(updateOptions.AppInternalBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);
    });
builder.Services.AddHttpClient(
    "updater-app-health",
    client =>
    {
        client.BaseAddress = new Uri(updateOptions.AppInternalBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
builder.Services.AddSingleton<IAppMaintenanceClient>(services =>
    new AppMaintenanceClient(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("updater-app-maintenance"),
        services.GetRequiredService<UpdaterTokenProvider>()));
builder.Services.AddSingleton<IHealthVerifier>(services =>
    new AppHealthVerifier(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("updater-app-health"),
        services.GetRequiredService<IAppMaintenanceClient>(),
        services.GetRequiredService<TimeProvider>()));

// 安装事务与后台队列。
builder.Services.AddSingleton<InstallRollbackService>();
builder.Services.AddSingleton<IInstallTransaction, InstallTransactionService>();
builder.Services.AddSingleton<InstallQueueService>();
builder.Services.AddHostedService(services => services.GetRequiredService<InstallQueueService>());
builder.Services.AddSingleton<IInstallCoordinator>(services => services.GetRequiredService<InstallQueueService>());

// 启动恢复：在任何新安装事务开始前收敛上次中断的非终态操作。
builder.Services.AddSingleton<IInstallRecovery, InstallRecovery>();
builder.Services.AddHostedService<InstallRecoveryHostedService>();

builder.Services.AddSingleton<IInstallService, InstallService>();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<JsonBadRequestExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/health/live");
app.MapUpdaterEndpoints();

app.Run();

public partial class Program;
