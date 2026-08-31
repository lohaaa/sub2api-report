using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Scheduling;

namespace Sub2ApiReport.Api.Endpoints;

internal static class ScheduleEndpoints
{
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/schedule")
            .RequireAuthorization()
            .WithTags("Schedule");

        group.MapGet("/", GetAsync)
            .WithName("GetReportSchedule")
            .WithSummary("获取月报计划")
            .WithDescription("返回数据库计划、持久化 trigger 同步状态和下次运行时间。")
            .Produces<ReportScheduleResponse>();

        group.MapPut("/", UpdateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("UpdateReportSchedule")
            .WithSummary("更新月报计划")
            .WithDescription("使用 revision 乐观并发更新计划，并立即对账 Quartz 持久化 trigger。")
            .Produces<ReportScheduleResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/run", RunNowAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("RunReportScheduleNow")
            .WithSummary("立即运行月报任务")
            .WithDescription("创建独立的排队执行记录并通过 Quartz 触发，不阻塞 HTTP 请求等待采集。")
            .Produces<ReportTaskRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/runs", GetRunsAsync)
            .WithName("GetReportTaskRuns")
            .WithSummary("获取任务执行记录")
            .WithDescription("分页返回计划、立即运行和重试产生的规范化执行记录。")
            .Produces<ReportTaskRunPageResponse>()
            .ProducesValidationProblem();

        group.MapPost("/runs/{runId:guid}/retry", RetryAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("RetryReportTaskRun")
            .WithSummary("重试失败任务")
            .WithDescription("创建关联原执行的新尝试，不覆盖原记录；发送结果未知时要求显式确认。")
            .Produces<ReportTaskRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<ReportScheduleResponse> GetAsync(
        IReportScheduleService scheduleService,
        CancellationToken cancellationToken) =>
        Map(await scheduleService.GetAsync(cancellationToken));

    private static async Task<IResult> UpdateAsync(
        UpdateReportScheduleRequest request,
        ClaimsPrincipal principal,
        IReportScheduleService scheduleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var schedule = await scheduleService.UpdateAsync(
                new UpdateReportScheduleCommand(
                    request.Enabled,
                    request.DayOfMonth,
                    request.ShortMonthStrategy,
                    request.LocalTime,
                    request.Timezone,
                    request.Windows?.Select(Map).ToArray(),
                    request.Revision),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.schedule.update",
                "monthly-report",
                schedule.Synchronized ? "succeeded" : "pending",
                httpContext.TraceIdentifier,
                $"{{\"revision\":{schedule.Revision},\"enabled\":{schedule.Enabled.ToString().ToLowerInvariant()}}}",
                cancellationToken);
            return TypedResults.Ok(Map(schedule));
        }
        catch (ReportScheduleConflictException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Configuration Conflict",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> RunNowAsync(
        ClaimsPrincipal principal,
        IReportScheduleService scheduleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await scheduleService.RunNowAsync(cancellationToken);
            await WriteQueuedAuditAsync(
                principal,
                auditWriter,
                httpContext,
                run,
                "report.schedule.run",
                cancellationToken);
            return TypedResults.Accepted(
                $"/api/v1/schedule/runs?runId={run.Id:D}",
                Map(run));
        }
        catch (ReportScheduleSynchronizationException exception)
        {
            return SchedulerUnavailable(exception.ErrorCode);
        }
    }

    private static async Task<IResult> GetRunsAsync(
        int? page,
        int? pageSize,
        IReportScheduleService scheduleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await scheduleService.GetRunsAsync(
                page ?? 1,
                pageSize ?? 20,
                cancellationToken);
            return TypedResults.Ok(new ReportTaskRunPageResponse(
                result.Items.Select(Map).ToArray(),
                result.Total,
                result.Page,
                result.PageSize,
                result.Pages));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> RetryAsync(
        Guid runId,
        RetryReportTaskRequest request,
        ClaimsPrincipal principal,
        IReportScheduleService scheduleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await scheduleService.RetryAsync(
                new RetryReportTaskCommand(runId, request.ConfirmOutcomeUnknown),
                cancellationToken);
            await WriteQueuedAuditAsync(
                principal,
                auditWriter,
                httpContext,
                run,
                "report.schedule.retry",
                cancellationToken);
            return TypedResults.Accepted(
                $"/api/v1/schedule/runs?runId={run.Id:D}",
                Map(run));
        }
        catch (ReportTaskRunNotFoundException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: "任务执行记录不存在。");
        }
        catch (ReportTaskOutcomeUnknownConfirmationRequiredException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Confirmation Required",
                detail: "存在发送结果未知的渠道，请确认可能重复发送后再重试。");
        }
        catch (ReportTaskRunNotRetryableException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Task Not Retryable",
                detail: exception.Message);
        }
        catch (ReportScheduleSynchronizationException exception)
        {
            return SchedulerUnavailable(exception.ErrorCode);
        }
    }

    private static Task WriteQueuedAuditAsync(
        ClaimsPrincipal principal,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        ReportTaskRunDocument run,
        string action,
        CancellationToken cancellationToken) => auditWriter.WriteAsync(
            principal.Identity?.Name,
            action,
            run.Id.ToString("D"),
            "queued",
            httpContext.TraceIdentifier,
            $"{{\"attempt\":{run.Attempt}}}",
            cancellationToken);

    private static ReportScheduleResponse Map(ReportScheduleDocument schedule) => new(
        schedule.Enabled,
        schedule.DayOfMonth,
        schedule.ShortMonthStrategy,
        schedule.LocalTime,
        schedule.Timezone,
        schedule.Windows.Select(window => new ReportWindowSpecResponse(
            window.Key,
            window.Kind,
            window.RollingDays,
            window.WeekStartsOn,
            window.CustomStartDate,
            window.CustomEndDate)).ToArray(),
        schedule.Revision,
        schedule.UpdatedAt,
        schedule.NextRunAt,
        schedule.Synchronized,
        schedule.SynchronizationErrorCode);

    private static ReportWindowSpec Map(ReportWindowSpecRequest window) => new(
        window.Key,
        window.Kind,
        window.RollingDays,
        window.WeekStartsOn,
        window.CustomStartDate,
        window.CustomEndDate);

    private static ReportTaskRunResponse Map(ReportTaskRunDocument run) => new(
        run.Id,
        run.Trigger,
        run.Status,
        run.ReportId,
        run.PeriodEnd,
        run.Timezone,
        run.ScheduleRevision,
        run.RetryOfRunId,
        run.Attempt,
        run.StartedAt,
        run.CollectingAt,
        run.RenderingAt,
        run.DeliveringAt,
        run.CompletedAt,
        run.ErrorCode,
        run.ErrorMessage,
        run.DeliveryCount,
        run.SucceededDeliveryCount,
        run.FailedDeliveryCount,
        run.HasOutcomeUnknown,
        run.CanRetry);

    private static ValidationProblem Validation(ArgumentException exception) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message],
        });

    private static ProblemHttpResult SchedulerUnavailable(string errorCode) => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Scheduler Unavailable",
        detail: "计划任务暂时无法进入持久化队列。",
        extensions: new Dictionary<string, object?> { ["code"] = errorCode });
}
