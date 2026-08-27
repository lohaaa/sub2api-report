using System.ComponentModel.DataAnnotations;
using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Api.Models;

/// <summary>Represents one notification channel with masked secrets.</summary>
public sealed record ChannelResponse(
    Guid Id,
    NotificationChannelType Type,
    string Name,
    bool Enabled,
    EmailChannelDisplayResponse? Email,
    WebhookChannelDisplayResponse? Webhook,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestCode);

/// <summary>Displays the non-secret email configuration of a channel.</summary>
public sealed record EmailChannelDisplayResponse(
    string Host,
    int Port,
    SmtpSecurityMode Security,
    string? Username,
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses,
    bool HasPassword,
    string? PasswordMask);

/// <summary>Displays only masked secret presence of a webhook channel.</summary>
public sealed record WebhookChannelDisplayResponse(
    bool HasWebhook,
    string? WebhookMask,
    string? SignSecretMask);

/// <summary>Creates a notification channel.</summary>
public sealed record CreateChannelRequest
{
    /// <summary>Gets the channel type.</summary>
    [Required]
    public required NotificationChannelType Type { get; init; }

    /// <summary>Gets the display name of the channel.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Gets whether the channel participates in deliveries.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the email settings; required for email channels.</summary>
    public EmailChannelInputRequest? Email { get; init; }

    /// <summary>Gets the optional SMTP password in plaintext.</summary>
    [StringLength(1024)]
    public string? SmtpPassword { get; init; }

    /// <summary>Gets the webhook URL; required for DingTalk and Feishu channels.</summary>
    [StringLength(2048)]
    public string? WebhookUrl { get; init; }

    /// <summary>Gets the webhook signing secret; required for DingTalk and Feishu channels.</summary>
    [StringLength(512)]
    public string? SignSecret { get; init; }
}

/// <summary>Contains the non-secret SMTP settings of an email channel.</summary>
public sealed record EmailChannelInputRequest
{
    /// <summary>Gets the SMTP host.</summary>
    [Required, StringLength(255, MinimumLength = 1)]
    public required string Host { get; init; }

    /// <summary>Gets the SMTP port.</summary>
    [Range(1, 65535)]
    public required int Port { get; init; }

    /// <summary>Gets the transport security mode.</summary>
    [Required]
    public required SmtpSecurityMode Security { get; init; }

    /// <summary>Gets the optional SMTP username.</summary>
    [StringLength(320)]
    public string? Username { get; init; }

    /// <summary>Gets the sender address.</summary>
    [Required, StringLength(320, MinimumLength = 3)]
    public required string FromAddress { get; init; }

    /// <summary>Gets the optional sender display name.</summary>
    [StringLength(200)]
    public string? FromName { get; init; }

    /// <summary>Gets the recipient addresses.</summary>
    [Required, MinLength(1)]
    public required IReadOnlyList<string> ToAddresses { get; init; }

    /// <summary>Gets the optional carbon-copy addresses.</summary>
    public IReadOnlyList<string>? CcAddresses { get; init; }
}

/// <summary>Contains the result of a channel test send.</summary>
public sealed record ChannelTestResponse(
    bool Succeeded,
    string Code,
    string Message,
    DateTimeOffset TestedAt);
