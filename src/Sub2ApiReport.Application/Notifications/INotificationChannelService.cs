using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Application.Notifications;

public interface INotificationChannelService
{
    Task<IReadOnlyList<NotificationChannelSummary>> ListAsync(CancellationToken cancellationToken);

    Task<NotificationChannelSummary> GetAsync(Guid channelId, CancellationToken cancellationToken);

    Task<NotificationChannelSummary> CreateAsync(
        CreateChannelCommand command,
        CancellationToken cancellationToken);

    Task<NotificationChannelSummary> UpdateAsync(
        UpdateChannelCommand command,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid channelId, CancellationToken cancellationToken);

    Task<ChannelTestOutcome> TestAsync(Guid channelId, CancellationToken cancellationToken);
}

public sealed record NotificationChannelSummary(
    Guid Id,
    NotificationChannelType Type,
    string Name,
    bool Enabled,
    EmailChannelDisplay? Email,
    WebhookChannelDisplay? Webhook,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestCode);

public sealed record EmailChannelDisplay(
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

public sealed record WebhookChannelDisplay(
    bool HasWebhook,
    string? WebhookMask,
    string? SignSecretMask);

public sealed record CreateChannelCommand(
    NotificationChannelType Type,
    string Name,
    bool Enabled,
    EmailChannelInput? Email,
    string? SmtpPassword,
    string? WebhookUrl,
    string? SignSecret);

public sealed record UpdateChannelCommand(
    Guid ChannelId,
    string Name,
    bool Enabled,
    EmailChannelInput? Email,
    bool ClearSmtpPassword,
    string? SmtpPassword,
    string? WebhookUrl,
    string? SignSecret,
    long ExpectedRevision);

public sealed record EmailChannelInput(
    string Host,
    int Port,
    SmtpSecurityMode Security,
    string? Username,
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    IReadOnlyList<string> CcAddresses);

public sealed record ChannelTestOutcome(
    bool Succeeded,
    string Code,
    string Message,
    DateTimeOffset TestedAt);

public sealed class NotificationChannelNotFoundException(Guid id)
    : InvalidOperationException($"The notification channel {id} does not exist.");

public sealed class NotificationChannelConflictException(long expectedRevision, long actualRevision)
    : InvalidOperationException(
        $"The notification channel revision changed from {expectedRevision} to {actualRevision}.");

public sealed class NotificationChannelInUseException(Guid id)
    : InvalidOperationException(
        $"The notification channel {id} has delivery records and cannot be deleted. Disable it instead.");
