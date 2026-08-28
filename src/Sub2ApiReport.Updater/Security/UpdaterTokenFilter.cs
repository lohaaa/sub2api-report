using System.Security.Cryptography;
using System.Text;

namespace Sub2ApiReport.Updater.Security;

/// <summary>
/// 内部 API 令牌校验。使用常量时间比较；令牌未配置或无效时一律拒绝（fail closed），并保证不记录令牌内容。
/// </summary>
internal sealed class UpdaterTokenFilter(UpdaterTokenProvider tokenProvider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!IsAuthorized(context.HttpContext))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "缺少或无效的 Updater 访问令牌。");
        }

        return await next(context);
    }

    private bool IsAuthorized(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedToken = header["Bearer ".Length..].Trim();
        return providedToken.Length > 0 && tokenProvider.Matches(providedToken);
    }
}
