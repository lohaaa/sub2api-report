using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.Api.Endpoints;

internal static class ReportDownloadEndpoints
{
    public static IEndpointRouteBuilder MapReportDownloadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/report-downloads/xlsx", DownloadAsync)
            .AllowAnonymous()
            .RequireRateLimiting("report-download")
            .WithTags("Report Downloads")
            .WithName("DownloadSharedReportXlsx")
            .WithSummary("使用限时授权下载报告 XLSX 工作簿")
            .WithDescription("校验投递消息中的限时令牌，并记录成功下载次数。")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    private static async Task<IResult> DownloadAsync(
        string token,
        IReportDownloadService downloadService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        SetPrivateDownloadHeaders(httpContext.Response);
        var attempt = await downloadService.DownloadAsync(token, cancellationToken);
        return attempt.Status switch
        {
            ReportDownloadAttemptStatus.Available => TypedResults.File(
                attempt.Content!,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                attempt.FileName,
                enableRangeProcessing: false),
            ReportDownloadAttemptStatus.Inactive => TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Download Link Expired",
                detail: "下载链接已过期、达到次数上限或被管理员撤销。"),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: "下载链接无效。"),
        };
    }

    private static void SetPrivateDownloadHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
    }
}
