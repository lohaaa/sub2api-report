namespace Sub2ApiReport.Api.Middleware;

internal static class SpaFallback
{
    public static async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/health"))
        {
            await WriteNotFoundAsync(context);
            return;
        }

        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var indexPath = Path.Combine(environment.WebRootPath, "index.html");
        if (!File.Exists(indexPath))
        {
            await WriteNotFoundAsync(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexPath, context.RequestAborted);
    }

    private static Task WriteNotFoundAsync(HttpContext context) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: "The requested resource was not found.",
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = context.TraceIdentifier,
            })
        .ExecuteAsync(context);
}
