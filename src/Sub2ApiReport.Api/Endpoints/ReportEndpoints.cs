using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Api.Endpoints;

internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reports")
            .RequireAuthorization()
            .WithTags("Reports");

        group.MapGet("/", GetPageAsync)
            .WithName("GetReports")
            .WithSummary("获取报告列表")
            .WithDescription("分页返回不可变报告快照的摘要。")
            .Produces<ReportPageResponse>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetReport")
            .WithSummary("获取报告详情")
            .WithDescription("返回指定报告保存时的 canonical snapshot。")
            .Produces<ReportDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/csv", GetCsvAsync)
            .WithName("DownloadReportCsv")
            .WithSummary("下载报告 CSV")
            .WithDescription("从不可变 canonical snapshot 生成带 UTF-8 BOM 的 CSV。")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/dry-run", GenerateDryRunAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("GenerateReportDryRun")
            .WithSummary("手工生成报告")
            .WithDescription("先自动刷新 Sub2API 用户与 Key，再采集 7/30 个完整自然日的用量并保存快照，不发送任何渠道。")
            .Produces<ReportDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/generations", GetGenerationRunsAsync)
            .WithName("GetReportGenerationRuns")
            .WithSummary("获取报告生成记录")
            .WithDescription("分页返回报告生成尝试，包括自动刷新失败的阶段与错误信息。")
            .Produces<ReportGenerationRunPageResponse>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}/deliveries", GetDeliveriesAsync)
            .WithName("GetReportDeliveries")
            .WithSummary("获取报告投递记录")
            .WithDescription("返回报告全部投递运行的分渠道与分片状态。")
            .Produces<IReadOnlyList<DeliveryRunResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deliveries", DeliverAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("DeliverReport")
            .WithSummary("手工投递报告")
            .WithDescription("把已保存的报告快照投递到选定渠道；部分完成报告需要显式确认。")
            .Produces<DeliveryRunResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deliveries/{runId:guid}/retry", RetryDeliveryAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("RetryReportDelivery")
            .WithSummary("补发失败的渠道")
            .WithDescription("只重试指定运行中失败的渠道，已成功渠道不会重复发送。")
            .Produces<DeliveryRunResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetPageAsync(
        int? page,
        int? pageSize,
        IReportService reportService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await reportService.GetPageAsync(
                page ?? 1,
                pageSize ?? 25,
                cancellationToken);
            return TypedResults.Ok(new ReportPageResponse(
                result.Items.Select(Map).ToArray(),
                result.Total,
                result.Page,
                result.PageSize,
                result.Pages));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = [exception.Message],
            });
        }
    }

    private static async Task<Results<Ok<ReportDetailResponse>, NotFound>> GetAsync(
        Guid id,
        IReportService reportService,
        CancellationToken cancellationToken)
    {
        var report = await reportService.GetAsync(id, cancellationToken);
        return report is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(Map(report));
    }

    private static async Task<IResult> GetCsvAsync(
        Guid id,
        IReportService reportService,
        CancellationToken cancellationToken)
    {
        var csv = await reportService.GetCsvAsync(id, cancellationToken);
        return csv is null
            ? TypedResults.NotFound()
            : TypedResults.File(
                csv.Content,
                "text/csv; charset=utf-8",
                csv.FileName,
                enableRangeProcessing: false);
    }

    private static async Task<IResult> GenerateDryRunAsync(
        GenerateReportRequest request,
        ClaimsPrincipal principal,
        IReportService reportService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await reportService.GenerateDryRunAsync(
                new GenerateReportCommand(
                    request.CutoffDate,
                    request.Windows?.Select(Map).ToArray()),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.generate.dry-run",
                report.ReportId.ToString("D"),
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"status\":\"{report.Status}\",\"failedRanges\":{report.Diagnostics.FailedRanges.Count}}}",
                cancellationToken);
            return TypedResults.Created($"/api/v1/reports/{report.ReportId:D}", Map(report));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "windows"] = [exception.Message],
            });
        }
        catch (Exception exception) when (exception is ReportGenerationPreconditionException or Sub2ApiConnectionNotConfiguredException or Sub2ApiUserScopeException or Sub2ApiConnectionConflictException)
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.generate.dry-run",
                "report",
                "rejected",
                httpContext.TraceIdentifier,
                $"{{\"code\":\"{DescribeErrorCode(exception)}\"}}",
                cancellationToken);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Report Preconditions Not Met",
                detail: exception.Message);
        }
        catch (Sub2ApiClientException exception)
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.generate.dry-run",
                "report",
                "failed",
                httpContext.TraceIdentifier,
                $"{{\"code\":\"{DescribeErrorCode(exception)}\"}}",
                cancellationToken);
            var statusCode = exception.Kind switch
            {
                Sub2ApiFailureKind.RateLimited => StatusCodes.Status503ServiceUnavailable,
                Sub2ApiFailureKind.Timeout => StatusCodes.Status504GatewayTimeout,
                Sub2ApiFailureKind.Unavailable => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            return TypedResults.Problem(
                statusCode: statusCode,
                title: "Report Generation Failed",
                detail: exception.Message);
        }
    }

    private static async Task<IResult> GetGenerationRunsAsync(
        int? page,
        int? pageSize,
        IReportService reportService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await reportService.GetGenerationRunsAsync(
                page ?? 1,
                pageSize ?? 20,
                cancellationToken);
            return TypedResults.Ok(new ReportGenerationRunPageResponse(
                result.Items.Select(item => new ReportGenerationRunItemResponse(
                    item.Id,
                    item.Trigger,
                    item.Status,
                    item.Stage,
                    item.ErrorCode,
                    item.ErrorMessage,
                    item.ConnectionRevision,
                    item.StartedAt,
                    item.CompletedAt,
                    item.ReportSnapshotId)).ToArray(),
                result.Total,
                result.Page,
                result.PageSize,
                result.Pages));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = [exception.Message],
            });
        }
    }

    private static string DescribeErrorCode(Exception exception) => exception switch
    {
        Sub2ApiClientException client => client.Kind switch
        {
            Sub2ApiFailureKind.Unauthorized => "unauthorized",
            Sub2ApiFailureKind.Forbidden => "forbidden",
            Sub2ApiFailureKind.Incompatible => "incompatible",
            Sub2ApiFailureKind.RateLimited => "rate-limited",
            Sub2ApiFailureKind.Timeout => "timeout",
            Sub2ApiFailureKind.Unavailable => "unavailable",
            _ => "invalid-response",
        },
        Sub2ApiUserScopeException => "user-scope",
        Sub2ApiConnectionNotConfiguredException => "connection-not-configured",
        Sub2ApiConnectionConflictException => "connection-changed",
        _ => "precondition",
    };

    private static async Task<Results<Ok<IReadOnlyList<DeliveryRunResponse>>, NotFound>> GetDeliveriesAsync(
        Guid id,
        IReportService reportService,
        IReportDeliveryService deliveryService,
        CancellationToken cancellationToken)
    {
        var report = await reportService.GetAsync(id, cancellationToken);
        if (report is null)
        {
            return TypedResults.NotFound();
        }

        var runs = await deliveryService.GetRunsAsync(id, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DeliveryRunResponse>>(runs.Select(MapRun).ToArray());
    }

    private static async Task<IResult> DeliverAsync(
        Guid id,
        DeliverReportRequest request,
        ClaimsPrincipal principal,
        IReportService reportService,
        IReportDeliveryService deliveryService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var report = await reportService.GetAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFoundReport();
        }

        try
        {
            var run = await deliveryService.DeliverAsync(
                new DeliverReportCommand(id, request.ChannelIds, request.ConfirmPartial),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "reports.deliver",
                id.ToString("D"),
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"runId\":\"{run.Id:D}\",\"channels\":{request.ChannelIds.Count},\"status\":\"{run.Status}\"}}",
                cancellationToken);
            return TypedResults.Created($"/api/v1/reports/{id:D}/deliveries/{run.Id:D}", MapRun(run));
        }
        catch (ReportDeliveryPreconditionException exception)
        {
            return DeliveryConflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return DeliveryValidation(exception);
        }
    }

    private static async Task<IResult> RetryDeliveryAsync(
        Guid id,
        Guid runId,
        ClaimsPrincipal principal,
        IReportDeliveryService deliveryService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await deliveryService.RetryAsync(
                new RetryDeliveryCommand(id, runId),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "reports.delivery.retry",
                run.Id.ToString("D"),
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"reportId\":\"{id:D}\",\"status\":\"{run.Status}\"}}",
                cancellationToken);
            return TypedResults.Ok(MapRun(run));
        }
        catch (ReportRunNotFoundException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: "报告或投递运行不存在。");
        }
        catch (ReportRunNotRetryableException exception)
        {
            return DeliveryConflict(exception.Message);
        }
        catch (ReportDeliveryPreconditionException exception)
        {
            return DeliveryConflict(exception.Message);
        }
    }

    private static DeliveryRunResponse MapRun(DeliveryRunDocument run) => new(
        run.Id,
        run.ReportId,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.Deliveries
            .Select(delivery => new DeliveryResponse(
                delivery.Id,
                delivery.ChannelId,
                delivery.ChannelType,
                delivery.ChannelName,
                delivery.Status,
                delivery.Attempts,
                delivery.ErrorCode,
                delivery.ErrorMessage,
                delivery.SentAt,
                delivery.Parts
                    .Select(part => new DeliveryPartResponse(
                        part.Index,
                        part.Count,
                        part.Status,
                        part.Attempts,
                        part.ErrorCode,
                        part.SentAt))
                    .ToArray()))
            .ToArray());

    private static ProblemHttpResult NotFoundReport() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not Found",
        detail: "报告不存在。");

    private static ProblemHttpResult DeliveryConflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict",
        detail: detail);

    private static ValidationProblem DeliveryValidation(ArgumentException exception) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message],
        });

    private static ReportListItemResponse Map(ReportListItem report) => new(
        report.Id,
        report.SchemaVersion,
        report.Status,
        report.Trigger,
        report.CutoffDate,
        report.Timezone,
        report.GeneratedAt,
        report.UserCount,
        report.KeyCount,
        report.FailedRangeCount,
        FormatDecimal(report.SevenDayActualCost),
        FormatDecimal(report.ThirtyDayActualCost),
        ReportWindowSummaryJson.Deserialize(report.WindowSummaryJson)
            .Select(summary => new ReportWindowListSummaryResponse(
                summary.Key,
                summary.Label,
                summary.StartDate,
                summary.EndDateExclusive,
                summary.DayCount,
                FormatDecimal(summary.TotalActualCost)))
            .ToArray());

    private static ReportDetailResponse Map(ReportDocument report) => new(
        report.SchemaVersion,
        report.ReportId,
        report.Status,
        report.Trigger,
        report.GeneratedAt,
        report.Timezone,
        report.ConnectionRevision,
        report.Windows.Select(Map).ToArray(),
        report.WindowTotals.Select(Map).ToArray(),
        report.Users.Select(user => new ReportUserUsageResponse(
            user.UserId,
            user.ExternalUserId,
            user.Username,
            user.Email,
            user.KeyCount,
            user.Windows.Select(Map).ToArray())).ToArray(),
        report.Keys.Select(key => new ReportKeyUsageResponse(
            key.KeyId,
            key.ExternalId,
            key.SourceUserId?.ToString(CultureInfo.InvariantCulture),
            key.SourceUserEmail,
            key.Name,
            key.Status,
            key.LastUsedAt,
            key.RetiredAt,
            key.Windows.Select(Map).ToArray())).ToArray(),
        new ReportDiagnosticsResponse(
            report.Diagnostics.FailedRanges.Select(failure => new ReportRangeFailureResponse(
                failure.ExternalUserId,
                failure.UserEmail,
                failure.ExternalKeyId,
                failure.KeyName,
                failure.WindowKey,
                failure.StartDate,
                failure.EndDateExclusive,
                failure.FailureKind,
                failure.ErrorCode)).ToArray()));

    private static ReportWindowSpec Map(ReportWindowSpecRequest window) => new(
        window.Key,
        window.Kind,
        window.RollingDays,
        window.WeekStartsOn,
        window.CustomStartDate,
        window.CustomEndDate);

    private static ReportWindowResponse Map(ReportWindowDescriptor window) => new(
        window.Key,
        window.Kind,
        window.RollingDays,
        window.WeekStartsOn,
        window.StartDate,
        window.EndDateExclusive,
        window.DayCount,
        window.Label);

    private static ReportWindowMetricsResponse Map(ReportWindowMetrics window) => new(
        window.WindowKey,
        Map(window.Metrics));
    private static ReportUsageMetricsResponse Map(ReportUsageMetrics metrics) => new(
        metrics.TotalRequests.ToString(CultureInfo.InvariantCulture),
        metrics.TotalInputTokens.ToString(CultureInfo.InvariantCulture),
        metrics.TotalOutputTokens.ToString(CultureInfo.InvariantCulture),
        metrics.TotalCacheTokens.ToString(CultureInfo.InvariantCulture),
        metrics.TotalCacheCreationTokens.ToString(CultureInfo.InvariantCulture),
        metrics.TotalCacheReadTokens.ToString(CultureInfo.InvariantCulture),
        metrics.TotalTokens.ToString(CultureInfo.InvariantCulture),
        FormatDecimal(metrics.TotalCost),
        FormatDecimal(metrics.TotalActualCost),
        FormatDecimal(metrics.AverageDurationMs));

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
