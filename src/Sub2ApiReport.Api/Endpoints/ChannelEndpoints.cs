using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.Notifications;

namespace Sub2ApiReport.Api.Endpoints;

internal static class ChannelEndpoints
{
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/channels")
            .RequireAuthorization()
            .WithTags("Channels");

        group.MapGet("", ListAsync)
            .WithName("ListChannels")
            .WithSummary("列出通知渠道")
            .WithDescription("返回全部通知渠道，秘密只以掩码显示。")
            .Produces<IReadOnlyList<ChannelResponse>>();

        group.MapPost("", CreateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("CreateChannel")
            .WithSummary("创建通知渠道")
            .Produces<ChannelResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{channelId:guid}", UpdateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("UpdateChannel")
            .WithSummary("更新通知渠道")
            .Produces<ChannelResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{channelId:guid}", DeleteAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("DeleteChannel")
            .WithSummary("删除通知渠道")
            .WithDescription("只允许删除没有投递记录的渠道；已有投递记录时请停用渠道。")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{channelId:guid}/test", TestAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("external")
            .WithName("TestChannel")
            .WithSummary("测试通知渠道")
            .WithDescription("向渠道发送一条合成测试消息，不包含真实报告数据。")
            .Produces<ChannelTestResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<ChannelResponse>>> ListAsync(
        INotificationChannelService channelService,
        CancellationToken cancellationToken)
    {
        var channels = await channelService.ListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<ChannelResponse>>(channels.Select(Map).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        CreateChannelRequest request,
        ClaimsPrincipal principal,
        INotificationChannelService channelService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var channel = await channelService.CreateAsync(
                new CreateChannelCommand(
                    request.Type,
                    request.Name,
                    request.Enabled,
                    request.Email is null
                        ? null
                        : ToEmailInput(request.Email),
                    request.SmtpPassword,
                    request.WebhookUrl,
                    request.SignSecret),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "channels.create",
                channel.Id,
                httpContext,
                $"{{\"type\":\"{channel.Type}\",\"name\":\"{EscapeJson(channel.Name)}\"}}",
                cancellationToken);
            return TypedResults.Created($"/api/v1/channels/{channel.Id}", Map(channel));
        }
        catch (NotificationChannelConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid channelId,
        ReplaceChannelRequest request,
        ClaimsPrincipal principal,
        INotificationChannelService channelService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var channel = await channelService.UpdateAsync(
                new UpdateChannelCommand(
                    channelId,
                    request.Name,
                    request.Enabled,
                    request.Email is null ? null : ToEmailInput(request.Email),
                    request.RemoveStoredPassword,
                    request.NewSmtpPassword,
                    request.WebhookUrl,
                    request.SignSecret,
                    request.Revision),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "channels.update",
                channel.Id,
                httpContext,
                $"{{\"revision\":{channel.Revision},\"enabled\":{channel.Enabled.ToString().ToLowerInvariant()}}}",
                cancellationToken);
            return TypedResults.Ok(Map(channel));
        }
        catch (NotificationChannelNotFoundException)
        {
            return NotFound("通知渠道不存在。");
        }
        catch (NotificationChannelConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid channelId,
        ClaimsPrincipal principal,
        INotificationChannelService channelService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await channelService.DeleteAsync(channelId, cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "channels.delete",
                channelId,
                httpContext,
                null,
                cancellationToken);
            return TypedResults.NoContent();
        }
        catch (NotificationChannelNotFoundException)
        {
            return NotFound("通知渠道不存在。");
        }
        catch (NotificationChannelInUseException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> TestAsync(
        Guid channelId,
        ClaimsPrincipal principal,
        INotificationChannelService channelService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await channelService.TestAsync(channelId, cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "channels.test",
                channelId,
                httpContext,
                $"{{\"succeeded\":{outcome.Succeeded.ToString().ToLowerInvariant()},\"code\":\"{EscapeJson(outcome.Code)}\"}}",
                cancellationToken);
            return TypedResults.Ok(new ChannelTestResponse(
                outcome.Succeeded,
                outcome.Code,
                outcome.Message,
                outcome.TestedAt));
        }
        catch (NotificationChannelNotFoundException)
        {
            return NotFound("通知渠道不存在。");
        }
    }

    private static EmailChannelInput ToEmailInput(EmailChannelInputRequest request) => new(
        request.Host,
        request.Port,
        request.Security,
        request.Username,
        request.FromAddress,
        request.FromName,
        request.ToAddresses,
        request.CcAddresses ?? []);

    private static ChannelResponse Map(NotificationChannelSummary channel) => new(
        channel.Id,
        channel.Type,
        channel.Name,
        channel.Enabled,
        channel.Email is null
            ? null
            : new EmailChannelDisplayResponse(
                channel.Email.Host,
                channel.Email.Port,
                channel.Email.Security,
                channel.Email.Username,
                channel.Email.FromAddress,
                channel.Email.FromName,
                channel.Email.ToAddresses,
                channel.Email.CcAddresses,
                channel.Email.HasPassword,
                channel.Email.PasswordMask),
        channel.Webhook is null
            ? null
            : new WebhookChannelDisplayResponse(
                channel.Webhook.HasWebhook,
                channel.Webhook.WebhookMask,
                channel.Webhook.SignSecretMask),
        channel.Revision,
        channel.CreatedAt,
        channel.UpdatedAt,
        channel.LastTestedAt,
        channel.LastTestSucceeded,
        channel.LastTestCode);

    private static ProblemHttpResult NotFound(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not Found",
        detail: detail);

    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict",
        detail: detail);

    private static ValidationProblem Validation(ArgumentException exception) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message],
        });

    private static string EscapeJson(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static Task WriteAuditAsync(
        IAuditWriter auditWriter,
        ClaimsPrincipal principal,
        string action,
        Guid targetId,
        HttpContext httpContext,
        string? metadataJson,
        CancellationToken cancellationToken) => auditWriter.WriteAsync(
            principal.Identity?.Name,
            action,
            targetId.ToString(),
            "succeeded",
            httpContext.TraceIdentifier,
            metadataJson,
            cancellationToken);
}
