using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.System;

namespace Sub2ApiReport.IntegrationTests;

public sealed class ApiSmokeTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LivenessEndpointIsAvailable()
    {
        using var response = await _client.GetAsync("/health/live", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VersionEndpointReturnsThePublicContract()
    {
        var response = await _client.GetFromJsonAsync<SystemVersionResponse>(
            "/api/v1/system/version",
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Version);
        Assert.Equal("stable", response.ReleaseChannel);
    }

    [Fact]
    public async Task DatabaseLogLevelChangeIsAppliedWithoutRestart()
    {
        SystemSettingsSnapshot updated;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
            var current = await settingsService.GetAsync(CancellationToken.None);
            updated = await settingsService.UpdateAsync(
                new UpdateSystemSettingsCommand(
                    current.Timezone,
                    current.ReleaseChannel,
                    "Warning",
                    current.ReportConcurrency,
                    current.ReportRetentionMonths,
                    current.BackupRetentionCount,
                    current.Revision),
                CancellationToken.None);
        }

        var levelSwitch = factory.Services.GetRequiredService<LoggingLevelSwitch>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (levelSwitch.MinimumLevel != LogEventLevel.Warning && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
        Assert.True(updated.Revision > 1);
    }

    [Fact]
    public async Task OpenApiIncludesManagementAndReportEndpoints()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", CancellationToken.None);

        Assert.Contains("/api/v1/sub2api/connection", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sub2api/keys/sync", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sub2api/users/scope", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/reports/generations", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/reports/dry-run", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/reports/{id}/csv", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownApiRouteIsNotHandledByTheSpaFallback()
    {
        using var response = await _client.GetAsync(
            "/api/v1/unknown",
            CancellationToken.None);
        var contentType = response.Content.Headers.ContentType?.MediaType;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", contentType);
    }
}
