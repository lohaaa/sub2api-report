using Microsoft.AspNetCore.Antiforgery;

namespace Sub2ApiReport.Api.Middleware;

internal static class AntiforgeryEndpointFilterExtensions
{
    public static RouteHandlerBuilder RequireAntiforgery(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "Antiforgery token 无效或已过期。");
            }

            return await next(context);
        });
}
