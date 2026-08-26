using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Security;

namespace Sub2ApiReport.Api.Endpoints;

internal static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/setup")
            .WithTags("Setup")
            .AllowAnonymous();

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetSetupStatus")
            .WithSummary("获取初始化状态")
            .WithDescription("返回实例是否仍需要创建首个管理员，不返回初始化码。")
            .Produces<SetupStatusResponse>();

        group.MapPost("/initialize", InitializeAsync)
            .WithName("InitializeAdministrator")
            .WithSummary("创建首个管理员")
            .WithDescription("使用 Docker 日志中的一次性初始化码创建唯一管理员。")
            .RequireAntiforgery()
            .RequireRateLimiting("setup")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<Ok<SetupStatusResponse>> GetStatusAsync(
        ISetupService setupService,
        CancellationToken cancellationToken)
    {
        var status = await setupService.GetStatusAsync(cancellationToken);
        return TypedResults.Ok(new SetupStatusResponse(
            status.SetupRequired,
            status.ChallengeExpiresAt,
            status.LockedUntil));
    }

    private static async Task<IResult> InitializeAsync(
        InitializeAdministratorRequest request,
        ISetupService setupService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await setupService.InitializeAsync(
            new InitializeAdministratorCommand(request.Code, request.Username, request.Password),
            httpContext.TraceIdentifier,
            cancellationToken);

        return result.Status switch
        {
            SetupInitializationStatus.Succeeded => TypedResults.NoContent(),
            SetupInitializationStatus.AlreadyInitialized => Problem(
                StatusCodes.Status409Conflict,
                "系统已经完成初始化。"),
            SetupInitializationStatus.Locked => Locked(httpContext),
            SetupInitializationStatus.Conflict => Problem(
                StatusCodes.Status409Conflict,
                "初始化状态已被其他请求更新。"),
            SetupInitializationStatus.InvalidAccount => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["account"] = [.. result.Errors] }),
            _ => Problem(
                StatusCodes.Status400BadRequest,
                result.Errors.Count > 0 ? result.Errors[0] : "初始化失败。"),
        };
    }

    private static ProblemHttpResult Locked(HttpContext httpContext)
    {
        httpContext.Response.Headers.RetryAfter = "300";
        return Problem(StatusCodes.Status429TooManyRequests, "初始化尝试已暂时锁定。");
    }

    private static ProblemHttpResult Problem(int statusCode, string detail) => TypedResults.Problem(
        statusCode: statusCode,
        title: statusCode == StatusCodes.Status409Conflict ? "Conflict" : "Bad Request",
        detail: detail);
}
