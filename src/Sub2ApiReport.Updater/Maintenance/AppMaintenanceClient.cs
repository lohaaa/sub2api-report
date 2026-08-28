using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Security;

namespace Sub2ApiReport.Updater.Maintenance;

/// <summary>
/// App 维护握手接口。App 侧端点（/internal/v1/update-handshake、/internal/v1/maintenance/*）
/// 属于后续 App 里程碑，此处仅定义 Updater 侧契约；测试使用注入的替身。
/// </summary>
public interface IAppMaintenanceClient
{
    /// <summary>获取 App 版本/契约/维护模式握手信息。App 不可达时抛出 UpdateOperationException。</summary>
    Task<AppUpdateHandshakeResponse> GetHandshakeAsync(CancellationToken cancellationToken);

    /// <summary>请求 App 进入维护模式（停止业务写入、等待活动任务结束）。</summary>
    Task EnterMaintenanceAsync(CancellationToken cancellationToken);

    /// <summary>通知 App 升级验证完成，解除维护模式。</summary>
    Task CompleteMaintenanceAsync(CancellationToken cancellationToken);
}

/// <summary>App 维护握手生产实现：只访问配置的 App 内部地址与固定路径，携带共享令牌。</summary>
public sealed class AppMaintenanceClient(HttpClient httpClient, UpdaterTokenProvider tokenProvider)
    : IAppMaintenanceClient
{
    private const string HandshakePath = "/internal/v1/update-handshake";
    private const string EnterMaintenancePath = "/internal/v1/maintenance/enter";
    private const string CompleteMaintenancePath = "/internal/v1/maintenance/complete";

    private static readonly JsonSerializerOptions ResponseOptions = new()
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

    public Task EnterMaintenanceAsync(CancellationToken cancellationToken) =>
        SendActionAsync(EnterMaintenancePath, cancellationToken);

    public Task CompleteMaintenanceAsync(CancellationToken cancellationToken) =>
        SendActionAsync(CompleteMaintenancePath, cancellationToken);

    private async Task SendActionAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
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

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, ResponseOptions, cancellationToken)
                .ConfigureAwait(false)
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
