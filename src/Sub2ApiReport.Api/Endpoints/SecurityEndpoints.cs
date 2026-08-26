using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Models;

namespace Sub2ApiReport.Api.Endpoints;

internal static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/security/antiforgery", GetAntiforgeryToken)
            .AllowAnonymous()
            .WithTags("Security")
            .WithName("GetAntiforgeryToken")
            .WithSummary("获取 antiforgery token")
            .WithDescription("设置安全 antiforgery cookie，并返回写请求 header 使用的 token。")
            .Produces<AntiforgeryTokenResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return TypedResults.Ok(new AntiforgeryTokenResponse(
            tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery token was not generated.")));
    }
}
