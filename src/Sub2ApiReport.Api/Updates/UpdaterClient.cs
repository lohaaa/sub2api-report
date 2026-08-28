using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Api.Updates;

internal sealed class UpdaterClientOptions
{
    public const string SectionName = "Updater";

    public string BaseUrl { get; set; } = "http://updater:8081";

    public string TokenFile { get; set; } = "/run/secrets/updater-token";
}

internal sealed class UpdaterUnavailableException(int statusCode, string detail)
    : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
}

internal sealed class UpdaterSharedTokenProvider(string tokenFile)
{
    private readonly object _lock = new();
    private string? _token;
    private bool _attempted;

    public string GetRequiredToken()
    {
        lock (_lock)
        {
            if (!_attempted)
            {
                _attempted = true;
                _token = Load();
            }

            return _token ?? throw new UpdaterUnavailableException(
                StatusCodes.Status503ServiceUnavailable,
                "Updater 共享令牌不可用。");
        }
    }

    public bool Matches(string provided)
    {
        string expected;
        try
        {
            expected = GetRequiredToken();
        }
        catch (UpdaterUnavailableException)
        {
            return false;
        }

        var actualBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private string? Load()
    {
        try
        {
            var value = File.ReadAllText(tokenFile).Trim();
            return value.Length == 64 && value.All(char.IsAsciiHexDigit) ? value : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal interface IUpdaterClient
{
    Task<UpdaterStatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    Task<UpdateCheckResponse> CheckAsync(string currentVersion, CancellationToken cancellationToken);

    Task<UpdatePlanResponse> GetPlanAsync(CancellationToken cancellationToken);

    Task<InstallAcceptedResponse> InstallAsync(
        string currentVersion,
        string? targetVersion,
        CancellationToken cancellationToken);

    Task<InstallOperationResponse?> GetOperationAsync(string operationId, CancellationToken cancellationToken);
}

internal sealed class UpdaterClient(HttpClient httpClient, UpdaterSharedTokenProvider tokenProvider)
    : IUpdaterClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public Task<UpdaterStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
        SendAsync<UpdaterStatusResponse>(HttpMethod.Get, "/internal/v1/status", null, cancellationToken);

    public Task<UpdateCheckResponse> CheckAsync(string currentVersion, CancellationToken cancellationToken) =>
        SendAsync<UpdateCheckResponse>(
            HttpMethod.Post,
            "/internal/v1/check",
            new UpdateCheckRequest(currentVersion),
            cancellationToken);

    public Task<UpdatePlanResponse> GetPlanAsync(CancellationToken cancellationToken) =>
        SendAsync<UpdatePlanResponse>(HttpMethod.Get, "/internal/v1/plan", null, cancellationToken);

    public Task<InstallAcceptedResponse> InstallAsync(
        string currentVersion,
        string? targetVersion,
        CancellationToken cancellationToken) =>
        SendAsync<InstallAcceptedResponse>(
            HttpMethod.Post,
            "/internal/v1/install",
            new InstallUpdateRequest(currentVersion, targetVersion),
            cancellationToken);

    public async Task<InstallOperationResponse?> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(
            HttpMethod.Get,
            $"/internal/v1/install/{Uri.EscapeDataString(operationId)}",
            null,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadSuccessAsync<InstallOperationResponse>(response, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(method, path, body, cancellationToken);
        return await ReadSuccessAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", tokenProvider.GetRequiredToken());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new UpdaterUnavailableException(
                StatusCodes.Status503ServiceUnavailable,
                "Updater 暂不可用。");
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string detail = "Updater 请求失败。";
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                    SerializerOptions,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                {
                    detail = problem.Detail;
                }
            }
            catch (JsonException)
            {
                // Keep the fixed safe message.
            }

            throw new UpdaterUnavailableException((int)response.StatusCode, detail);
        }

        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
            ?? throw new UpdaterUnavailableException(
                StatusCodes.Status502BadGateway,
                "Updater 返回了无效响应。");
    }
}
