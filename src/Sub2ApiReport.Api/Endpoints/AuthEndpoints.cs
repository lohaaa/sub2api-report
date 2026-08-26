using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Infrastructure.Identity;

namespace Sub2ApiReport.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireAntiforgery()
            .RequireRateLimiting("login")
            .WithName("LoginAdministrator")
            .WithSummary("管理员登录")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/recover", RecoverAsync)
            .AllowAnonymous()
            .RequireAntiforgery()
            .RequireRateLimiting("recovery")
            .WithName("RecoverAdministrator")
            .WithSummary("使用主机恢复码重置密码")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/me", GetCurrentAdministrator)
            .RequireAuthorization()
            .WithName("GetCurrentAdministrator")
            .WithSummary("获取当前管理员会话")
            .Produces<CurrentAdministratorResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .WithName("LogoutAdministrator")
            .WithSummary("退出管理员会话")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .RequireRateLimiting("password")
            .WithName("ChangeAdministratorPassword")
            .WithSummary("修改管理员密码")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPost("/step-up", StepUpAsync)
            .RequireAuthorization()
            .RequireAntiforgery()
            .RequireRateLimiting("password")
            .WithName("CreateStepUpAuthorization")
            .WithSummary("确认密码并创建短时高风险操作授权")
            .Produces<CurrentAdministratorResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        SignInManager<Administrator> signInManager,
        UserManager<Administrator> userManager,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var administrator = await userManager.FindByNameAsync(username);
        var result = administrator is null
            ? SignInResult.Failed
            : await signInManager.CheckPasswordSignInAsync(
                administrator,
                request.Password,
                lockoutOnFailure: true);

        if (!result.Succeeded || administrator is null)
        {
            await auditWriter.WriteAsync(
                username,
                "auth.login",
                "administrator",
                result.IsLockedOut ? "locked" : "failed",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "用户名或密码无效。");
        }

        await signInManager.SignOutAsync();
        await signInManager.SignInAsync(administrator, isPersistent: false, authenticationMethod: "Password");
        await auditWriter.WriteAsync(
            administrator.UserName,
            "auth.login",
            "administrator",
            "succeeded",
            httpContext.TraceIdentifier,
            null,
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RecoverAsync(
        RecoverAdministratorRequest request,
        IRecoveryService recoveryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await recoveryService.RecoverAsync(
            new RecoverAdministratorCommand(request.Username, request.Code, request.NewPassword),
            httpContext.TraceIdentifier,
            cancellationToken);

        return result.Status switch
        {
            AccountRecoveryStatus.Succeeded => TypedResults.NoContent(),
            AccountRecoveryStatus.Locked => Locked(httpContext, "恢复尝试已暂时锁定。"),
            AccountRecoveryStatus.InvalidAccount => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["account"] = [.. result.Errors] }),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: result.Errors.Count > 0 ? result.Errors[0] : "密码恢复失败。"),
        };
    }

    private static Ok<CurrentAdministratorResponse> GetCurrentAdministrator(
        ClaimsPrincipal principal,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TypedResults.Ok(CreateSessionResponse(principal, timeProvider.GetUtcNow()));
    }

    private static async Task<NoContent> LogoutAsync(
        ClaimsPrincipal principal,
        SignInManager<Administrator> signInManager,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await signInManager.SignOutAsync();
        await auditWriter.WriteAsync(
            principal.Identity?.Name,
            "auth.logout",
            "administrator",
            "succeeded",
            httpContext.TraceIdentifier,
            null,
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<Administrator> userManager,
        SignInManager<Administrator> signInManager,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var administrator = await userManager.GetUserAsync(principal);
        if (administrator is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(
            administrator,
            request.CurrentPassword,
            request.NewPassword);
        if (!result.Succeeded)
        {
            await auditWriter.WriteAsync(
                administrator.UserName,
                "auth.password.change",
                "administrator",
                "failed",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(error => error.Description).ToArray(),
            });
        }

        await signInManager.RefreshSignInAsync(administrator);
        await auditWriter.WriteAsync(
            administrator.UserName,
            "auth.password.change",
            "administrator",
            "succeeded",
            httpContext.TraceIdentifier,
            null,
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> StepUpAsync(
        StepUpRequest request,
        ClaimsPrincipal principal,
        UserManager<Administrator> userManager,
        IAuditWriter auditWriter,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var administrator = await userManager.GetUserAsync(principal);
        if (administrator is null || !await userManager.CheckPasswordAsync(administrator, request.Password))
        {
            await auditWriter.WriteAsync(
                principal.Identity?.Name,
                "auth.step-up",
                "administrator",
                "failed",
                httpContext.TraceIdentifier,
                null,
                cancellationToken);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "当前密码无效。");
        }

        if (principal.Identity is ClaimsIdentity identity)
        {
            foreach (var claim in identity.FindAll(SecurityClaimTypes.StepUpAt).ToArray())
            {
                identity.RemoveClaim(claim);
            }

            identity.AddClaim(new Claim(
                SecurityClaimTypes.StepUpAt,
                timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        await httpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
        await auditWriter.WriteAsync(
            administrator.UserName,
            "auth.step-up",
            "administrator",
            "succeeded",
            httpContext.TraceIdentifier,
            null,
            cancellationToken);
        return TypedResults.Ok(CreateSessionResponse(principal, timeProvider.GetUtcNow()));
    }

    private static CurrentAdministratorResponse CreateSessionResponse(
        ClaimsPrincipal principal,
        DateTimeOffset now)
    {
        var sessionStartedAt = ReadTimestamp(principal, SecurityClaimTypes.SessionStartedAt) ?? now;
        var stepUpAt = ReadTimestamp(principal, SecurityClaimTypes.StepUpAt);
        var stepUpExpiresAt = stepUpAt?.Add(SecuritySessionDefaults.StepUpLifetime);
        if (stepUpExpiresAt <= now)
        {
            stepUpExpiresAt = null;
        }

        return new CurrentAdministratorResponse(
            principal.Identity?.Name ?? string.Empty,
            sessionStartedAt,
            stepUpExpiresAt);
    }

    private static DateTimeOffset? ReadTimestamp(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static ProblemHttpResult Locked(HttpContext httpContext, string detail)
    {
        httpContext.Response.Headers.RetryAfter = "300";
        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too Many Requests",
            detail: detail);
    }
}
