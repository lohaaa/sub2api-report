using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Application.Notifications;

/// <summary>Renders and delivers report messages for one channel type.</summary>
public interface IReportSender
{
    NotificationChannelType ChannelType { get; }

    /// <summary>Renders deterministic outbound message parts for a report.</summary>
    IReadOnlyList<OutboundPart> Render(ReportDocument report, ChannelDeliveryContext context);

    /// <summary>Delivers one rendered part. Returns a failure outcome instead of throwing.</summary>
    Task<ChannelSendOutcome> SendPartAsync(
        OutboundPart part,
        ChannelDeliveryContext context,
        CancellationToken cancellationToken);

    /// <summary>Sends a synthetic test message. Returns a failure outcome instead of throwing.</summary>
    Task<ChannelSendOutcome> SendTestAsync(
        ChannelDeliveryContext context,
        CancellationToken cancellationToken);
}

/// <summary>Runtime configuration and secrets for one channel delivery.</summary>
public sealed record ChannelDeliveryContext(
    Guid ChannelId,
    string ChannelName,
    NotificationChannelType ChannelType,
    EmailDeliveryOptions? Email,
    WebhookDeliveryOptions? Webhook)
{
    public static ChannelDeliveryContext ForEmail(
        Guid channelId,
        string channelName,
        EmailDeliveryOptions email) => new(channelId, channelName, NotificationChannelType.Email, email, null);

    public static ChannelDeliveryContext ForWebhook(
        Guid channelId,
        string channelName,
        NotificationChannelType type,
        WebhookDeliveryOptions webhook) => new(channelId, channelName, type, null, webhook);
}

/// <summary>Non-secret and secret delivery settings of an email channel after decryption.</summary>
public sealed record EmailDeliveryOptions(
    string Host,
    int Port,
    SmtpSecurityMode Security,
    string? Username,
    string? Password,
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses);

/// <summary>Decrypted webhook credentials of a DingTalk or Feishu channel.</summary>
public sealed record WebhookDeliveryOptions(string Url, string SignSecret);

/// <summary>One rendered outbound message of a channel delivery.</summary>
public sealed record OutboundPart(
    int Index,
    int Count,
    string Subject,
    string Body,
    string? CsvContent,
    string PayloadHash);

/// <summary>Result of one channel send attempt with sanitized error information.</summary>
public sealed record ChannelSendOutcome(bool Succeeded, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static readonly ChannelSendOutcome Ok = new(true);

    public static ChannelSendOutcome Fail(string errorCode, string? errorMessage = null) =>
        new(false, errorCode, errorMessage);
}
