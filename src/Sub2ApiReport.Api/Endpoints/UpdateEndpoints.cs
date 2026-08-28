using System.Security.Claims;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Updates;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Api.Endpoints;

internal sealed record InstallSystemUpdateRequest(bool Confirm, string? TargetVersion);

internal static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/updates")
            .WithTags("Updates")
            .RequireAuthorization();

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetUpdateStatus")
            .Produces<UpdaterStatusResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/check", CheckAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("updates")
            .WithName("CheckForUpdates")
            .Produces<UpdateCheckResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/plan", GetPlanAsync)
            .WithName("GetUpdatePlan")
            .Produces<UpdatePlanResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/install", InstallAsync)
            .RequireAntiforgery()
            .RequireRecentStepUp()
            .RequireRateLimiting("update-install")
            .WithName("InstallSystemUpdate")
            .Produces<InstallAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/operations/{operationId}", GetOperationAsync)
            .WithName("GetUpdateOperation")
            .Produces<InstallOperationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        var internalGroup = endpoints.MapGroup("/internal/v1")
            .WithTags("Internal Updates")
            .AddEndpointFilter<InternalUpdaterTokenFilter>();

        internalGroup.MapGet("/update-handshake", GetHandshakeAsync)
            .Produces<AppUpdateHandshakeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        internalGroup.MapPost("/maintenance/enter", EnterMaintenanceAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        internalGroup.MapPost("/maintenance/complete", CompleteMaintenanceAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IUpdaterClient updaterClient,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => updaterClient.GetStatusAsync(cancellationToken));

    private static async Task<IResult> CheckAsync(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        ISystemInfoService systemInfoService,
        IUpdaterClient updaterClient,
        IAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            var version = await systemInfoService.GetVersionAsync(cancellationToken);
            var response = await updaterClient.CheckAsync(version.Version, cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "update.check",
                response.AvailableVersion ?? "latest",
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"updateAvailable\":{response.UpdateAvailable.ToString().ToLowerInvariant()}}}",
                cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (UpdaterUnavailableException exception)
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "update.check",
                "latest",
                "failed",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetPlanAsync(
        IUpdaterClient updaterClient,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => updaterClient.GetPlanAsync(cancellationToken));

    private static async Task<IResult> InstallAsync(
        InstallSystemUpdateRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        ISystemInfoService systemInfoService,
        IUpdaterClient updaterClient,
        IAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!request.Confirm)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["confirm"] = ["必须明确确认升级影响。"],
            });
        }

        try
        {
            var version = await systemInfoService.GetVersionAsync(cancellationToken);
            var response = await updaterClient.InstallAsync(
                version.Version,
                request.TargetVersion,
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "update.install.request",
                response.OperationId,
                "accepted",
                httpContext.TraceIdentifier,
                request.TargetVersion is null ? null : $"{{\"targetVersion\":\"{request.TargetVersion}\"}}",
                cancellationToken);
            return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
        }
        catch (UpdaterUnavailableException exception)
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "update.install.request",
                request.TargetVersion ?? "latest",
                "rejected",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetOperationAsync(
        string operationId,
        IUpdaterClient updaterClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await updaterClient.GetOperationAsync(operationId, cancellationToken);
            return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
        }
        catch (UpdaterUnavailableException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetHandshakeAsync(
        MaintenanceCoordinator coordinator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await coordinator.GetHandshakeAsync(cancellationToken));

    private static async Task<IResult> EnterMaintenanceAsync(
        AppMaintenanceRequest request,
        MaintenanceCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.EnterAsync(request.OperationId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (MaintenanceConflictException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["operationId"] = [exception.Message],
            });
        }
    }

    private static async Task<IResult> CompleteMaintenanceAsync(
        AppMaintenanceRequest request,
        MaintenanceCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.CompleteAsync(request.OperationId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (MaintenanceConflictException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["operationId"] = [exception.Message],
            });
        }
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return TypedResults.Ok(await action());
        }
        catch (UpdaterUnavailableException exception)
        {
            return Problem(exception);
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Problem(
        UpdaterUnavailableException exception) =>
        TypedResults.Problem(
            statusCode: exception.StatusCode,
            title: exception.StatusCode switch
            {
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
                _ => "Bad Gateway",
            },
            detail: exception.Message);
}
