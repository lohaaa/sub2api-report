using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Endpoints;
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
