using Microsoft.AspNetCore.Diagnostics;

namespace Sub2ApiReport.Updater.Middleware;

/// <summary>
/// 将请求体 JSON 反序列化失败（含未知字段拒绝）映射为 400 Problem Details，而不是 500。
/// </summary>
internal sealed class JsonBadRequestExceptionHandler(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest || badRequest.StatusCode != 400)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "请求体 JSON 无效或包含未知字段。",
            },
        });
    }
}
