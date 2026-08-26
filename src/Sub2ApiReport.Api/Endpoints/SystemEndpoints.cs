using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.System;

namespace Sub2ApiReport.Api.Endpoints;

internal static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system")
            .WithTags("System");

        group.MapGet("/version", GetVersion)
            .AllowAnonymous()
            .WithName("GetSystemVersion")
            .WithSummary("获取系统版本")
            .WithDescription("返回当前应用版本、运行环境和发布通道。")
            .Produces<SystemVersionResponse>();

        group.MapGet("/settings", GetSettingsAsync)
            .RequireAuthorization()
            .WithName("GetSystemSettings")
            .WithSummary("获取动态系统设置")
            .Produces<SystemSettingsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/settings", UpdateSettingsAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("UpdateSystemSettings")
            .WithSummary("更新动态系统设置")
            .Produces<SystemSettingsResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<Ok<SystemVersionResponse>> GetVersion(
        ISystemInfoService systemInfoService,
        CancellationToken cancellationToken)
    {
        var version = await systemInfoService.GetVersionAsync(cancellationToken);
        return TypedResults.Ok(new SystemVersionResponse(
            version.Version,
            version.Environment,
            version.ReleaseChannel));
    }

    private static async Task<Ok<SystemSettingsResponse>> GetSettingsAsync(
        ISystemSettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        return TypedResults.Ok(Map(settings));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateSystemSettingsRequest request,
        ClaimsPrincipal principal,
        ISystemSettingsService settingsService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await settingsService.UpdateAsync(
                new UpdateSystemSettingsCommand(
                    request.Timezone,
                    request.ReleaseChannel,
                    request.LogLevel,
                    request.ReportConcurrency,
                    request.ReportRetentionMonths,
                    request.BackupRetentionCount,
                    request.Revision),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "system.settings.update",
                "system-settings",
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"revision\":{settings.Revision}}}",
                cancellationToken);
            return TypedResults.Ok(Map(settings));
        }
        catch (SystemSettingsConflictException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "系统设置已被其他请求修改，请刷新后重试。");
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "settings"] = [exception.Message],
            });
        }
    }

    private static SystemSettingsResponse Map(SystemSettingsSnapshot settings) => new(
        settings.Timezone,
        settings.ReleaseChannel,
        settings.LogLevel,
        settings.ReportConcurrency,
        settings.ReportRetentionMonths,
        settings.BackupRetentionCount,
        settings.Revision,
        settings.UpdatedAt);
}
