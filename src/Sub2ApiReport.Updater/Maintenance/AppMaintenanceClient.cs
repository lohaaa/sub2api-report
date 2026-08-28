using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Security;

namespace Sub2ApiReport.Updater.Maintenance;

public interface IAppMaintenanceClient
{
    Task<AppUpdateHandshakeResponse> GetHandshakeAsync(CancellationToken cancellationToken);

    Task EnterMaintenanceAsync(string operationId, CancellationToken cancellationToken);

    Task CompleteMaintenanceAsync(string operationId, CancellationToken cancellationToken);
}

public sealed class AppMaintenanceClient(HttpClient httpClient, UpdaterTokenProvider tokenProvider)
    : IAppMaintenanceClient
{
    private const string HandshakePath = "/internal/v1/update-handshake";
    private const string EnterMaintenancePath = "/internal/v1/maintenance/enter";
    private const string CompleteMaintenancePath = "/internal/v1/maintenance/complete";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    public async Task<AppUpdateHandshakeResponse> GetHandshakeAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, HandshakePath);
        return await SendForJsonAsync<AppUpdateHandshakeResponse>(request, cancellationToken);
    }

    public Task EnterMaintenanceAsync(string operationId, CancellationToken cancellationToken) =>
        SendActionAsync(EnterMaintenancePath, operationId, cancellationToken);

    public Task CompleteMaintenanceAsync(string operationId, CancellationToken cancellationToken) =>
        SendActionAsync(CompleteMaintenancePath, operationId, cancellationToken);

    private async Task SendActionAsync(string path, string operationId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new AppMaintenanceRequest(operationId), options: SerializerOptions);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "App 维护请求失败。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "App 维护请求失败。",
                exception);
        }
    }

    private async Task<TResponse> SendForJsonAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "App 维护握手失败。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken)
                ?? throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "App 维护握手返回无效数据。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not UpdateOperationException
                and (HttpRequestException or JsonException or InvalidOperationException))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "App 维护握手返回无效数据。",
                exception);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var token = tokenProvider.GetBearerToken()
            ?? throw new UpdateOperationException(
                StatusCodes.Status503ServiceUnavailable,
                "Updater 共享令牌不可用，拒绝执行 App 维护握手。");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }
}
