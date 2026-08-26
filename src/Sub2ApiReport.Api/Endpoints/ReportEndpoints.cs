using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;

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
            .WithDescription("采集 7/30 个完整自然日的 Key 用量并保存快照，不发送任何渠道。")
            .Produces<ReportDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
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
                new GenerateReportCommand(request.CutoffDate),
                cancellationToken);
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.generate.dry-run",
                report.ReportId.ToString("D"),
                "succeeded",
                httpContext.TraceIdentifier,
                $"{{\"status\":\"{report.Status}\",\"failedSegments\":{report.Diagnostics.FailedSegments.Count},\"unassignedSegments\":{report.Diagnostics.UnassignedSegments.Count}}}",
                cancellationToken);
            return TypedResults.Created($"/api/v1/reports/{report.ReportId:D}", Map(report));
        }
        catch (Exception exception) when (exception is ReportGenerationPreconditionException or Sub2ApiConnectionNotConfiguredException)
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "report.generate.dry-run",
                "report",
                "rejected",
                httpContext.TraceIdentifier,
                "{\"code\":\"precondition\"}",
                cancellationToken);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Report Preconditions Not Met",
                detail: exception.Message);
        }
    }

    private static ReportListItemResponse Map(ReportListItem report) => new(
        report.Id,
        report.SchemaVersion,
        report.Status,
        report.Trigger,
        report.CutoffDate,
        report.Timezone,
        report.GeneratedAt,
        report.PersonCount,
        report.KeyCount,
        report.FailedSegmentCount,
        report.UnassignedSegmentCount,
        FormatDecimal(report.SevenDayActualCost),
        FormatDecimal(report.ThirtyDayActualCost));

    private static ReportDetailResponse Map(ReportDocument report) => new(
        report.SchemaVersion,
        report.ReportId,
        report.Status,
        report.Trigger,
        report.GeneratedAt,
        report.Timezone,
        report.ConnectionRevision,
        Map(report.SevenDayWindow),
        Map(report.ThirtyDayWindow),
        Map(report.SevenDayTotal),
        Map(report.ThirtyDayTotal),
        report.People.Select(person => new ReportPersonUsageResponse(
            person.PersonId,
            person.Code,
            person.DisplayName,
            person.KeyCount,
            Map(person.SevenDay),
            Map(person.ThirtyDay))).ToArray(),
        report.Keys.Select(key => new ReportKeyUsageResponse(
            key.KeyId,
            key.ExternalId,
            key.Name,
            key.Status,
            key.LastUsedAt,
            key.RetiredAt,
            Map(key.SevenDay),
            Map(key.ThirtyDay),
            key.Segments.Select(segment => new ReportKeySegmentResponse(
                segment.StartDate,
                segment.EndDate,
                segment.PersonId,
                segment.PersonCode,
                segment.PersonDisplayName,
                segment.Metrics is null ? null : Map(segment.Metrics),
                segment.FailureKind,
                segment.DiagnosticCode)).ToArray())).ToArray(),
        new ReportDiagnosticsResponse(
            report.Diagnostics.FailedSegments.Select(Map).ToArray(),
            report.Diagnostics.UnassignedSegments.Select(Map).ToArray(),
            report.Diagnostics.ConflictingSegments.Select(Map).ToArray(),
            report.Diagnostics.ZeroUsageKeyIds));

    private static ReportWindowResponse Map(ReportWindow window) => new(
        window.Days,
        window.StartDate,
        window.EndDate);

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

    private static ReportSegmentDiagnosticResponse Map(ReportSegmentDiagnostic diagnostic) => new(
        diagnostic.ExternalKeyId,
        diagnostic.KeyName,
        diagnostic.StartDate,
        diagnostic.EndDate,
        diagnostic.Code,
        diagnostic.FailureKind);
}
