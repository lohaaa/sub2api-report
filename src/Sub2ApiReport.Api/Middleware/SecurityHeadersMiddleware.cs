namespace Sub2ApiReport.Api.Middleware;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["Content-Security-Policy"] =
                "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; object-src 'none'; "
                + "img-src 'self' data:; font-src 'self'; connect-src 'self'; "
                + "script-src 'self'; style-src 'self' 'unsafe-inline'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = context.Request.Path.StartsWithSegments(
                "/api/v1/report-downloads",
                StringComparison.Ordinal)
                ? "no-referrer"
                : "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });

        await next(context);
    }
}
