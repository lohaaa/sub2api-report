using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class AuthenticationFlowTests
{
    private const string Username = "synthetic-admin";
    private const string Password = "ValidPassword1!";
    private const string NewPassword = "NewValidPassword2!";

    [Fact]
    public async Task StateChangingEndpointRejectsMissingAntiforgeryToken()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/v1/setup/initialize", new
        {
            code = "0000-0000-0000-0000-0000-0000-0000-0000",
            username = Username,
            password = Password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ConcurrentInitializationCreatesOnlyOneAdministrator()
    {
        await using var factory = new ApiWebApplicationFactory();
        var issue = await RotateSetupCodeAsync(factory);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);

        var firstRequest = await CreateJsonRequestAsync(
            firstClient,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = issue.Code, username = Username, password = Password });
        var secondRequest = await CreateJsonRequestAsync(
            secondClient,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = issue.Code, username = Username, password = Password });

        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest),
            secondClient.SendAsync(secondRequest));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var statusCodes = new[] { firstResponse.StatusCode, secondResponse.StatusCode };

        Assert.Contains(HttpStatusCode.NoContent, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(1, dbContext.Users.Count());
    }

    [Fact]
    public async Task LoginCookieAndAntiforgeryProtectTheSession()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);

        using var loginRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username = Username, password = Password });
        using var loginResponse = await client.SendAsync(loginRequest);

        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        var cookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("sub2api-report-dev=", StringComparison.Ordinal)
            && !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        using var currentResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);

        using var stepUpRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/step-up",
            new { password = Password });
        using var stepUpResponse = await client.SendAsync(stepUpRequest);
        Assert.Equal(HttpStatusCode.OK, stepUpResponse.StatusCode);
        var steppedUpSession = await stepUpResponse.Content.ReadFromJsonAsync<CurrentAdministratorResponse>();
        Assert.NotNull(steppedUpSession?.StepUpExpiresAt);

        using var unprotectedLogout = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, unprotectedLogout.StatusCode);

        using var logoutRequest = await CreateJsonRequestAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/auth/logout",
            body: null);
        using var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var signedOutResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, signedOutResponse.StatusCode);
    }

    [Fact]
    public async Task ProductionCookieUsesHostPrefixAndSecureFlags()
    {
        await using var factory = new ApiWebApplicationFactory(
            databasePath: null,
            deleteDatabaseOnDispose: true,
            environmentName: Microsoft.Extensions.Hosting.Environments.Production);
        await InitializeAsync(factory);
        using var client = CreateClient(factory);

        using var response = await LoginRequestAsync(client, Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-sub2api-report=", StringComparison.Ordinal)
            && !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordChangeInvalidatesTheOldPassword()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, Password);

        using var changeRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/change-password",
            new { currentPassword = Password, newPassword = NewPassword });
        using var changeResponse = await client.SendAsync(changeRequest);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var anotherClient = CreateClient(factory);
        using var oldLogin = await LoginRequestAsync(anotherClient, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await LoginRequestAsync(anotherClient, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);
    }

    [Fact]
    public async Task HostRecoveryCodeResetsThePasswordOnce()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        SecretCodeIssue issue;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var recoveryService = scope.ServiceProvider.GetRequiredService<IRecoveryService>();
            issue = Assert.IsType<SecretCodeIssue>(await recoveryService.CreateChallengeAsync(
                "integration-test",
                CancellationToken.None));
        }

        using var client = CreateClient(factory);
        using var recoveryRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/recover",
            new { username = Username, code = issue.Code, newPassword = NewPassword });
        using var recoveryResponse = await client.SendAsync(recoveryRequest);
        Assert.Equal(HttpStatusCode.NoContent, recoveryResponse.StatusCode);

        using var reusedRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/recover",
            new { username = Username, code = issue.Code, newPassword = Password });
        using var reusedResponse = await client.SendAsync(reusedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, reusedResponse.StatusCode);
        using var loginResponse = await LoginRequestAsync(client, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedSettingsUpdateUsesRevisionConcurrency()
    {
        await using var factory = new ApiWebApplicationFactory();
        await InitializeAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, Password);
        var current = await client.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/system/settings");
        Assert.NotNull(current);

        var update = new
        {
            timezone = "UTC",
            releaseChannel = "preview",
            logLevel = "Warning",
            reportConcurrency = 6,
            reportRetentionMonths = 24,
            backupRetentionCount = 20,
            reportExternalBaseUrl = "https://reports.example.com",
            reportDownloadLinkHours = 48,
            reportDownloadMaxDownloads = 20,
            revision = current.Revision,
        };
        using var updateRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Put,
            "/api/v1/system/settings",
            update);
        using var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var staleRequest = await CreateJsonRequestAsync(
            client,
            HttpMethod.Put,
            "/api/v1/system/settings",
            update);
        using var staleResponse = await client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
    }

    [Fact]
    public async Task RestartRotatesAnUnusedSetupCode()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sub2api-report-restart-{Guid.NewGuid():N}.db");
        SecretCodeIssue firstIssue;
        await using (var firstFactory = new ApiWebApplicationFactory(databasePath, false))
        {
            firstIssue = await RotateSetupCodeAsync(firstFactory);
        }

        await using var secondFactory = new ApiWebApplicationFactory(databasePath, true);
        using var client = CreateClient(secondFactory);
        using var request = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = firstIssue.Code, username = Username, password = Password });
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task InitializeAsync(ApiWebApplicationFactory factory)
    {
        var issue = await RotateSetupCodeAsync(factory);
        using var client = CreateClient(factory);
        using var request = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/setup/initialize",
            new { code = issue.Code, username = Username, password = Password });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<SecretCodeIssue> RotateSetupCodeAsync(ApiWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setupService = scope.ServiceProvider.GetRequiredService<ISetupService>();
        return Assert.IsType<SecretCodeIssue>(await setupService.RotateChallengeOnStartupAsync(
            CancellationToken.None));
    }

    private static async Task LoginAsync(HttpClient client, string password)
    {
        using var response = await LoginRequestAsync(client, password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginRequestAsync(HttpClient client, string password)
    {
        using var request = await CreateJsonRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username = Username, password });
        return await client.SendAsync(request);
    }

    private static async Task<HttpRequestMessage> CreateJsonRequestAsync<T>(
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

        return request;
    }
}
