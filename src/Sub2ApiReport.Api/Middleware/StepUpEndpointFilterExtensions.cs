using System.Globalization;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Security;

namespace Sub2ApiReport.Api.Middleware;

internal static class StepUpEndpointFilterExtensions
{
    public static RouteHandlerBuilder RequireRecentStepUp(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
            var value = httpContext.User.FindFirst(SecurityClaimTypes.StepUpAt)?.Value;
            var validTimestamp = long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds);
            var now = timeProvider.GetUtcNow();
            var isCurrent = validTimestamp
                && DateTimeOffset.FromUnixTimeSeconds(seconds) + SecuritySessionDefaults.StepUpLifetime > now;
            if (isCurrent)
            {
                return await next(context);
            }

            var auditWriter = httpContext.RequestServices.GetRequiredService<IAuditWriter>();
            await auditWriter.WriteAsync(
                httpContext.User.Identity?.Name,
                "auth.step-up.required",
                httpContext.Request.Path,
                "denied",
                httpContext.TraceIdentifier,
                null,
                httpContext.RequestAborted);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Step-up Required",
                detail: "请先确认当前管理员密码，再执行此敏感操作。");
        });
}
