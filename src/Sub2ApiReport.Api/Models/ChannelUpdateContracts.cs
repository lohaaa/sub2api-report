using System.ComponentModel.DataAnnotations;
using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Api.Models;

/// <summary>Replaces the settings of a notification channel using optimistic concurrency.</summary>
public sealed record ReplaceChannelRequest
{
    /// <summary>Gets the display name of the channel.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Gets whether the channel participates in deliveries.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the email settings; required for email channels.</summary>
    public EmailChannelInputRequest? Email { get; init; }

    /// <summary>Gets whether the stored SMTP password should be removed.</summary>
    public required bool RemoveStoredPassword { get; init; }

    /// <summary>Gets the new SMTP password; empty keeps the existing one.</summary>
    [StringLength(1024)]
    public string? NewSmtpPassword { get; init; }

    /// <summary>Gets the new webhook URL; empty keeps the existing one.</summary>
    [StringLength(2048)]
    public string? WebhookUrl { get; init; }

    /// <summary>Gets the new signing secret; empty keeps the existing one.</summary>
    [StringLength(512)]
    public string? SignSecret { get; init; }

    /// <summary>Gets the expected revision of the channel.</summary>
    [Range(1, long.MaxValue)]
    public required long Revision { get; init; }
}
