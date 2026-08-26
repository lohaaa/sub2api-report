using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Sub2Api;

namespace Sub2ApiReport.Api.Endpoints;

internal static class Sub2ApiEndpoints
{
    public static IEndpointRouteBuilder MapSub2ApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sub2api")
            .RequireAuthorization()
            .WithTags("Sub2API");

        group.MapGet("/connection", GetConnectionAsync)
            .WithName("GetSub2ApiConnection")
            .WithSummary("获取 Sub2API 连接配置")
            .WithDescription("返回连接地址、目标标识和密钥掩码，不返回 Admin API Key 明文。")
            .Produces<Sub2ApiConnectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/connection", SaveConnectionAsync)
            .RequireAntiforgery()
            .RequireRecentStepUp()
            .RequireRateLimiting("configuration")
            .WithName("SaveSub2ApiConnection")
            .WithSummary("保存 Sub2API 连接配置")
            .WithDescription("保存动态连接配置。替换或清除 Admin API Key 需要短时高风险操作授权。")
            .Produces<Sub2ApiConnectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/connection/test", TestConnectionAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("TestSub2ApiConnection")
            .WithSummary("测试 Sub2API 连接")
            .WithDescription("使用已保存的密钥探测目标用户的 API Key 分页接口。")
            .Produces<Sub2ApiConnectionTestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/keys", GetKeysAsync)
            .WithName("GetSub2ApiKeyInventory")
            .WithSummary("获取已同步的 API Key 清单")
            .WithDescription("分页返回脱敏 Key 快照、有效期归属和完整性诊断。")
            .Produces<ApiKeyInventoryPageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/keys/sync", SynchronizeKeysAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("SynchronizeSub2ApiKeys")
            .WithSummary("同步 Sub2API Key")
            .WithDescription("完整读取上游分页后，以单个数据库事务更新本地快照。")
            .Produces<KeySynchronizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    private static async Task<Ok<Sub2ApiConnectionResponse>> GetConnectionAsync(
        ISub2ApiConnectionService connectionService,
        CancellationToken cancellationToken)
    {
        var connection = await connectionService.GetAsync(cancellationToken);
        return TypedResults.Ok(Map(connection));
    }

    private static async Task<IResult> SaveConnectionAsync(
        SaveSub2ApiConnectionRequest request,
        ClaimsPrincipal principal,
        ISub2ApiConnectionService connectionService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!long.TryParse(
                    request.UserId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var userId)
                || (request.CodexGroupId is not null
                    && !long.TryParse(
                        request.CodexGroupId,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out _)))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["identifier"] = ["Sub2API 用户或分组 ID 无效。"],
                });
            }

            var codexGroupId = request.CodexGroupId is null
                ? (long?)null
                : long.Parse(request.CodexGroupId, CultureInfo.InvariantCulture);
            var connection = await connectionService.SaveAsync(
                new SaveSub2ApiConnectionCommand(
                    request.BaseUrl,
                    request.AdminApiKey,
                    request.ClearAdminApiKey,
                    userId,
                    codexGroupId,
                    request.Revision),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "sub2api.connection.update",
                "sub2api-connection",
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"revision\":{connection.Revision},\"hasSecret\":{connection.HasAdminApiKey.ToString().ToLowerInvariant()}}}",
                cancellationToken);
            return TypedResults.Ok(Map(connection));
        }
        catch (Sub2ApiConnectionConflictException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "Sub2API 连接配置已被其他请求修改，请刷新后重试。");
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "connection"] = [exception.Message],
            });
        }
    }

    private static async Task<IResult> TestConnectionAsync(
        ClaimsPrincipal principal,
        ISub2ApiConnectionService connectionService,
        ISub2ApiClient client,
        IAuditWriter auditWriter,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Sub2ApiConnectionCredentials connection;
        try
        {
            connection = await connectionService.GetCredentialsAsync(cancellationToken);
        }
        catch (Sub2ApiConnectionNotConfiguredException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Connection Not Configured",
                detail: "请先保存完整的 Sub2API 连接配置。");
        }

        try
        {
            var probe = await client.TestAsync(connection, cancellationToken);
            try
            {
                await connectionService.RecordTestResultAsync(true, "connected", cancellationToken);
            }
            catch (Sub2ApiConnectionConflictException)
            {
                return ConnectionChanged();
            }

            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "sub2api.connection.test",
                "sub2api-connection",
                "succeeded",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return TypedResults.Ok(new Sub2ApiConnectionTestResponse(
                true,
                "connected",
                "连接成功。",
                probe.AvailableKeyCount,
                timeProvider.GetUtcNow()));
        }
        catch (Sub2ApiClientException exception)
        {
            var (code, message) = DescribeFailure(exception.Kind);
            try
            {
                await connectionService.RecordTestResultAsync(false, code, cancellationToken);
            }
            catch (Sub2ApiConnectionConflictException)
            {
                return ConnectionChanged();
            }

            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "sub2api.connection.test",
                "sub2api-connection",
                "failed",
                httpContext.TraceIdentifier,
                $"{{\"code\":\"{code}\"}}",
                cancellationToken);
            return TypedResults.Ok(new Sub2ApiConnectionTestResponse(
                false,
                code,
                message,
                null,
                timeProvider.GetUtcNow()));
        }
    }

    private static async Task<IResult> GetKeysAsync(
        int? page,
        int? pageSize,
        bool? unmappedOnly,
        IKeyInventoryService inventoryService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inventoryService.GetPageAsync(
                new ApiKeyInventoryQuery(
                    page ?? 1,
                    pageSize ?? 50,
                    unmappedOnly ?? false),
                cancellationToken);
            return TypedResults.Ok(new ApiKeyInventoryPageResponse(
                result.Items.Select(item => new ApiKeyInventoryItemResponse(
                    item.Id,
                    item.ExternalId.ToString(CultureInfo.InvariantCulture),
                    item.Name,
                    item.Status,
                    item.GroupId?.ToString(CultureInfo.InvariantCulture),
                    item.LastUsedAt,
                    item.LastSeenAt,
                    item.RetiredAt,
                    item.Assignments.Select(PeopleEndpoints.Map).ToArray()))
                    .ToArray(),
                result.Total,
                result.Page,
                result.PageSize,
                result.Pages,
                new ApiKeyInventoryDiagnosticsResponse(
                    result.Diagnostics.UnmappedKeys,
                    result.Diagnostics.OverlappingAssignments,
                    result.Diagnostics.RetiredKeys),
                result.LastSynchronizedAt));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = [exception.Message],
            });
        }
    }

    private static async Task<IResult> SynchronizeKeysAsync(
        ClaimsPrincipal principal,
        IKeyInventoryService inventoryService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inventoryService.SynchronizeAsync(cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "sub2api.keys.synchronize",
                "external-api-keys",
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"added\":{result.Added},\"updated\":{result.Updated},\"retired\":{result.Retired},\"total\":{result.Total}}}",
                cancellationToken);
            return TypedResults.Ok(new KeySynchronizationResponse(
                result.Added,
                result.Updated,
                result.Retired,
                result.Total,
                result.SynchronizedAt,
                result.ConfigurationRevision));
        }
        catch (Sub2ApiConnectionNotConfiguredException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Connection Not Configured",
                detail: "请先保存完整的 Sub2API 连接配置。");
        }
        catch (Sub2ApiConnectionConflictException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Connection Changed",
                detail: "同步期间连接配置发生变化，请重新同步。");
        }
        catch (Sub2ApiClientException exception)
        {
            var (code, message) = DescribeFailure(exception.Kind);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "sub2api.keys.synchronize",
                "external-api-keys",
                "failed",
                httpContext.TraceIdentifier,
                $"{{\"code\":\"{code}\"}}",
                cancellationToken);
            var statusCode = exception.Kind switch
            {
                Sub2ApiFailureKind.RateLimited => StatusCodes.Status503ServiceUnavailable,
                Sub2ApiFailureKind.Timeout => StatusCodes.Status504GatewayTimeout,
                Sub2ApiFailureKind.Unavailable => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            if (exception.RetryAfter is { } retryAfter)
            {
                httpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return TypedResults.Problem(
                statusCode: statusCode,
                title: "Sub2API Synchronization Failed",
                detail: message,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }
    }

    private static ProblemHttpResult ConnectionChanged() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Connection Changed",
        detail: "连接测试期间配置发生变化，请重新测试。");

    internal static (string Code, string Message) DescribeFailure(Sub2ApiFailureKind kind) => kind switch
    {
        Sub2ApiFailureKind.Unauthorized => ("unauthorized", "Admin API Key 无效。"),
        Sub2ApiFailureKind.Forbidden => ("forbidden", "Admin API Key 没有读取目标用户 Key 的权限。"),
        Sub2ApiFailureKind.Incompatible => ("incompatible", "当前 Sub2API 部署不支持所需的 Key 同步接口。"),
        Sub2ApiFailureKind.RateLimited => ("rate-limited", "Sub2API 暂时限流，请稍后重试。"),
        Sub2ApiFailureKind.Timeout => ("timeout", "连接 Sub2API 超时。"),
        Sub2ApiFailureKind.Unavailable => ("unavailable", "Sub2API 当前不可用。"),
        _ => ("invalid-response", "Sub2API 返回了无法识别的数据。"),
    };

    private static Sub2ApiConnectionResponse Map(Sub2ApiConnectionSnapshot? connection) => connection is null
        ? new Sub2ApiConnectionResponse(
            false,
            null,
            false,
            null,
            null,
            null,
            0,
            null,
            null,
            null,
            null,
            null,
            null)
        : new Sub2ApiConnectionResponse(
            connection.HasAdminApiKey,
            connection.BaseUrl,
            connection.HasAdminApiKey,
            connection.AdminApiKeyMask,
            connection.UserId.ToString(CultureInfo.InvariantCulture),
            connection.CodexGroupId?.ToString(CultureInfo.InvariantCulture),
            connection.Revision,
            connection.UpdatedAt,
            connection.LastTestedAt,
            connection.LastTestSucceeded,
            connection.LastTestCode,
            connection.LastSynchronizedAt,
            connection.LastSynchronizedKeyCount);
}
