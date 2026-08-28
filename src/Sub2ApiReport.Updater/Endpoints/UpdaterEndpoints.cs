using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Security;
using Sub2ApiReport.Updater.Services;
using Sub2ApiReport.Updater.State;

namespace Sub2ApiReport.Updater.Endpoints;

internal static class UpdaterEndpoints
{
    public static IEndpointRouteBuilder MapUpdaterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/v1")
            .WithTags("Updater")
            .AddEndpointFilter<UpdaterTokenFilter>();

        group.MapPost("/check", CheckAsync)
            .WithName("CheckUpdaterRelease")
            .WithSummary("检查固定仓库的最新稳定 Release")
            .Accepts<UpdateCheckRequest>("application/json")
            .Produces<UpdateCheckResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/plan", GetPlanAsync)
            .WithName("GetUpdatePlan")
            .WithSummary("查看基于最近检查结果的升级计划（安装保持关闭）")
            .Produces<UpdatePlanResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetUpdaterStatus")
            .WithSummary("获取 Updater 版本与持久化状态")
            .Produces<UpdaterStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/install", InstallAsync)
            .WithName("InstallUpdaterRelease")
            .WithSummary("发起在线安装（配置门禁开启后）")
            .Accepts<InstallUpdateRequest>("application/json")
            .Produces<InstallAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/install/{operationId}", GetInstallOperationAsync)
            .WithName("GetUpdaterInstallOperation")
            .WithSummary("查询安装操作状态（供 App 轮询）")
            .Produces<InstallOperationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CheckAsync(
        UpdateCheckService checkService,
        UpdateCheckRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await checkService.CheckAsync(request, cancellationToken));
        }
        catch (UpdateOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<Results<Ok<UpdatePlanResponse>, ProblemHttpResult>> GetPlanAsync(
        UpdateStateStore stateStore,
        UpdateOptions options,
        CancellationToken cancellationToken)
    {
        var snapshot = await stateStore.LoadStatusAsync(cancellationToken);
        if (snapshot?.AvailableVersion is null || snapshot.LastError is not null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "暂无有效的更新检查结果，请先执行检查。");
        }

        var steps = new List<UpdatePlanStep>
        {
            new(1, "preflight", "校验 manifest 签名、版本、架构与部署契约。"),
            new(2, "download-archive", "流式下载 App 镜像归档并限制总大小。"),
            new(3, "verify-archive", "校验归档 SHA-256 与大小。"),
            new(4, "load-image", "加载镜像并校验 image ID（安装启用后执行）。"),
            new(5, "backup", "执行 SQLite 一致性备份。"),
            new(6, "replace-app", "替换 App 容器并执行数据库迁移。"),
            new(7, "verify", "健康验证，失败时自动回滚。"),
        };

        return TypedResults.Ok(new UpdatePlanResponse(
            snapshot.CurrentVersion ?? UpdaterVersion.GetCurrent(),
            snapshot.AvailableVersion,
            options.InstallationEnabled,
            snapshot.ManualUpgradeRequired,
            steps));
    }

    private static async Task<IResult> InstallAsync(
        IInstallService installService,
        InstallUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await installService.SubmitAsync(request, cancellationToken);
        if (result.Accepted && result.Operation is not null)
        {
            return Results.Json(
                new InstallAcceptedResponse(result.Operation.OperationId, result.Operation.State),
                statusCode: StatusCodes.Status202Accepted);
        }

        return TypedResults.Problem(
            statusCode: result.StatusCode,
            title: result.StatusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                _ => "Conflict",
            },
            detail: result.Detail ?? "安装请求被拒绝。");
    }

    private static async Task<Results<Ok<InstallOperationResponse>, NotFound>> GetInstallOperationAsync(
        UpdateStateStore stateStore,
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = await stateStore.LoadOperationAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new InstallOperationResponse(
            operation.OperationId,
            operation.State,
            InstallOperationStates.IsTerminal(operation.State) ? null : operation.State,
            operation.CurrentVersion,
            operation.TargetVersion,
            operation.CreatedAt,
            operation.UpdatedAt,
            operation.CompletedAt,
            operation.LastError,
            operation.Stages));
    }

    private static async Task<Ok<UpdaterStatusResponse>> GetStatusAsync(
        UpdateStateStore stateStore,
        UpdateOptions options,
        CancellationToken cancellationToken)
    {
        var snapshot = await stateStore.LoadStatusAsync(cancellationToken);
        var state = snapshot switch
        {
            null => "idle",
            _ when snapshot.LastError is not null => "check_failed",
            _ when snapshot.UpdateAvailable => "update_available",
            _ => "up_to_date",
        };

        var operations = await stateStore.LoadAllOperationsAsync(cancellationToken);
        var lastOperation = operations.OrderByDescending(operation => operation.CreatedAt).FirstOrDefault();

        return TypedResults.Ok(new UpdaterStatusResponse(
            UpdaterVersion.GetCurrent(),
            options.InstallationEnabled,
            state,
            snapshot?.LastCheckedAt,
            snapshot?.AvailableVersion,
            lastOperation?.OperationId,
            lastOperation?.State));
    }

    private static ProblemHttpResult Problem(UpdateOperationException exception)
    {
        var title = exception.StatusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status502BadGateway => "Bad Gateway",
            _ => "Internal Server Error",
        };

        return TypedResults.Problem(
            statusCode: exception.StatusCode,
            title: title,
            detail: exception.Message);
    }
}
