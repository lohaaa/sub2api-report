using System.Text.Json;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal static class ChannelRuntimeMapper
{
    public static ChannelDeliveryContext CreateContext(
        NotificationChannel channel,
        ChannelSecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.Type switch
        {
            NotificationChannelType.Email => ChannelDeliveryContext.ForEmail(
                channel.Id,
                channel.Name,
                CreateEmailOptions(channel, protector)),
            NotificationChannelType.DingTalk => ChannelDeliveryContext.ForWebhook(
                channel.Id,
                channel.Name,
                NotificationChannelType.DingTalk,
                CreateWebhookOptions(channel, protector)),
            NotificationChannelType.Feishu => ChannelDeliveryContext.ForWebhook(
                channel.Id,
                channel.Name,
                NotificationChannelType.Feishu,
                CreateWebhookOptions(channel, protector)),
            _ => throw new InvalidOperationException(
                $"The notification channel type {channel.Type} is not supported."),
        };
    }

    private static EmailDeliveryOptions CreateEmailOptions(
        NotificationChannel channel,
        ChannelSecretProtector protector)
    {
        if (channel.SmtpHost is null
            || channel.SmtpPort is not { } port
            || channel.SmtpSecurity is not { } security
            || channel.FromAddress is null
            || channel.ToAddressesJson is null)
        {
            throw new InvalidOperationException("The email channel is not fully configured.");
        }

        return new EmailDeliveryOptions(
            channel.SmtpHost,
            port,
            security,
            channel.SmtpUsername,
            channel.SmtpPasswordCiphertext is { } passwordCiphertext
                ? protector.Unprotect(passwordCiphertext)
                : null,
            channel.FromAddress,
            channel.FromName,
            ParseAddresses(channel.ToAddressesJson),
            ParseAddresses(channel.CcAddressesJson) ?? []);
    }

    private static WebhookDeliveryOptions CreateWebhookOptions(
        NotificationChannel channel,
        ChannelSecretProtector protector)
    {
        if (channel.WebhookCiphertext is null || channel.SignSecretCiphertext is null)
        {
            throw new InvalidOperationException("The webhook channel is not fully configured.");
        }

        return new WebhookDeliveryOptions(
            protector.Unprotect(channel.WebhookCiphertext),
            protector.Unprotect(channel.SignSecretCiphertext));
    }

    private static string[] ParseAddresses(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The stored channel address list is invalid.",
                exception);
        }
    }
}
