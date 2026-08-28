using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Sub2ApiReport.Api.Endpoints;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Services;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Infrastructure;
using Sub2ApiReport.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var loggingLevelSwitch = new LoggingLevelSwitch();
builder.Services.AddSingleton(loggingLevelSwitch);
builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.ControlledBy(loggingLevelSwitch)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient.notifications", LogEventLevel.Warning)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] =
            context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var configuredConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? DatabaseDefaults.ConnectionString;
var connectionString = DatabaseDefaults.ResolveConnectionString(
    configuredConnectionString,
    builder.Environment.ContentRootPath);
var useSecureCookies = !builder.Environment.IsDevelopment();
var configuredDataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine("data", "keys");
var dataProtectionKeysPath = DatabaseDefaults.ResolvePath(
    configuredDataProtectionKeysPath,
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("Sub2ApiReport")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.AddQuartz(options =>
{
    options.SchedulerName = "Sub2ApiReport";
    options.SchedulerId = "AUTO";
    options.UseDefaultThreadPool(maxConcurrency: 1);
    options.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.PerformSchemaValidation = true;
        store.UseSystemTextJsonSerializer();
        store.UseMicrosoftSQLite(connectionString);
    });
});
builder.Services.AddQuartzServer(
    options => options.WaitForJobsToComplete = true,
    healthCheckTags: ["ready"]);
_ = builder.Services.AddInfrastructure(connectionString);
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = useSecureCookies
        ? "__Host-sub2api-report"
        : "sub2api-report-dev";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = useSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        var startedAtValue = context.Principal?.FindFirst(SecurityClaimTypes.SessionStartedAt)?.Value;
        var validStartedAt = long.TryParse(
            startedAtValue,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var startedAt);
        var timeProvider = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        if (validStartedAt
            && timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(startedAt)
                <= TimeSpan.FromHours(24))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    };
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = useSecureCookies
        ? "__Host-sub2api-report-antiforgery"
        : "sub2api-report-antiforgery-dev";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = useSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, _) =>
    {
        await TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too Many Requests",
            detail: "请求过于频繁，请稍后重试。",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = context.HttpContext.TraceIdentifier,
            })
            .ExecuteAsync(context.HttpContext);
    };
    AddRateLimitPolicy(options, "setup", 10, TimeSpan.FromMinutes(5));
    AddRateLimitPolicy(options, "login", 10, TimeSpan.FromMinutes(1));
    AddRateLimitPolicy(options, "password", 5, TimeSpan.FromMinutes(5));
    AddRateLimitPolicy(options, "recovery", 5, TimeSpan.FromMinutes(5));
    AddRateLimitPolicy(options, "configuration", 20, TimeSpan.FromMinutes(1));
    AddRateLimitPolicy(options, "external", 6, TimeSpan.FromMinutes(1));
    AddRateLimitPolicy(options, "report-download", 60, TimeSpan.FromMinutes(1));
});
builder.Services.AddScoped<ISystemInfoService, SystemInfoService>();
builder.Services.AddHostedService<DatabaseSettingsSynchronizer>();
builder.Services.AddHostedService<SetupCodeBootstrapService>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live", "ready"])
    .AddDbContextCheck<ReportDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

_ = app.MapSecurityEndpoints()
    .MapSetupEndpoints()
    .MapAuthEndpoints()
    .MapSystemEndpoints()
    .MapReportDownloadEndpoints()
    .MapSub2ApiEndpoints()
    .MapReportEndpoints()
    .MapChannelEndpoints()
    .MapScheduleEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var cacheControl = context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache, no-store"
            : "public, max-age=31536000, immutable";
        context.Context.Response.Headers.CacheControl = cacheControl;
    },
});

app.MapFallback(SpaFallback.HandleAsync);

app.Run();

static void AddRateLimitPolicy(
    RateLimiterOptions options,
    string policyName,
    int permitLimit,
    TimeSpan window) =>
    options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

public partial class Program;
