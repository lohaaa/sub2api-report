using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal sealed class DatabaseNotificationChannelService(
    ReportDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    IEnumerable<IReportSender> senders) : INotificationChannelService
{
    private readonly Dictionary<NotificationChannelType, IReportSender> _senders =
        senders.ToDictionary(sender => sender.ChannelType);

    private static readonly Dictionary<NotificationChannelType, string> WebhookHosts =
        new()
        {
            [NotificationChannelType.DingTalk] = "oapi.dingtalk.com",
            [NotificationChannelType.Feishu] = "open.feishu.cn",
        };

    private readonly ChannelSecretProtector _protector = new(dataProtectionProvider);

    public async Task<IReadOnlyList<NotificationChannelSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var channels = await dbContext.NotificationChannels
            .AsNoTracking()
            .OrderBy(channel => channel.CreatedAt)
            .ThenBy(channel => channel.Id)
            .ToListAsync(cancellationToken);
        return channels.Select(Map).ToArray();
    }

    public async Task<NotificationChannelSummary> GetAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await FindAsync(channelId, cancellationToken);
        return Map(channel);
    }

    public async Task<NotificationChannelSummary> CreateAsync(
        CreateChannelCommand command,
        CancellationToken cancellationToken)
    {
        var (settings, secrets) = BuildChannelMaterial(
            command.Type,
            command.Email,
            command.SmtpPassword,
            clearPassword: false,
            command.WebhookUrl,
            command.SignSecret,
            existingPasswordCiphertext: null,
            existingPasswordSuffix: null,
            existingWebhookCiphertext: null,
            existingWebhookSuffix: null,
            existingSignSecretCiphertext: null,
            existingSignSecretSuffix: null);
        var channel = NotificationChannel.Create(
            command.Type,
            command.Name,
            command.Enabled,
            settings,
            secrets,
            timeProvider.GetUtcNow());
        dbContext.NotificationChannels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(channel);
    }

    public async Task<NotificationChannelSummary> UpdateAsync(
        UpdateChannelCommand command,
        CancellationToken cancellationToken)
    {
        var channel = await FindTrackedAsync(command.ChannelId, cancellationToken);
        if (channel.Revision != command.ExpectedRevision)
        {
            throw new NotificationChannelConflictException(command.ExpectedRevision, channel.Revision);
        }

        var (settings, secrets) = BuildChannelMaterial(
            channel.Type,
            command.Email,
            command.SmtpPassword,
            command.ClearSmtpPassword,
            command.WebhookUrl,
            command.SignSecret,
            channel.SmtpPasswordCiphertext,
            channel.SmtpPasswordSuffix,
            channel.WebhookCiphertext,
            channel.WebhookSuffix,
            channel.SignSecretCiphertext,
            channel.SignSecretSuffix);
        channel.Update(
            command.Name,
            command.Enabled,
            settings,
            secrets,
            timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new NotificationChannelConflictException(
                command.ExpectedRevision,
                command.ExpectedRevision + 1);
        }

        return Map(channel);
    }

    public async Task DeleteAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await FindTrackedAsync(channelId, cancellationToken);
        var hasDeliveries = await dbContext.DeliveryRecords
            .AnyAsync(delivery => delivery.ChannelId == channelId, cancellationToken);
        if (hasDeliveries)
        {
            throw new NotificationChannelInUseException(channelId);
        }

        dbContext.NotificationChannels.Remove(channel);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChannelTestOutcome> TestAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await FindTrackedAsync(channelId, cancellationToken);
        var context = ChannelRuntimeMapper.CreateContext(channel, _protector);
        var sender = ResolveSender(channel.Type);
        var outcome = await sender.SendTestAsync(context, cancellationToken);
        var testedAt = timeProvider.GetUtcNow();
        channel.RecordTest(
            outcome.Succeeded,
            outcome.Succeeded ? "ok" : outcome.ErrorCode ?? "failed",
            testedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ChannelTestOutcome(
            outcome.Succeeded,
            outcome.Succeeded ? "ok" : outcome.ErrorCode ?? "failed",
            outcome.Succeeded ? "测试消息发送成功。" : outcome.ErrorMessage ?? "测试消息发送失败。",
            testedAt);
    }

    private IReportSender ResolveSender(NotificationChannelType type)
    {
        var sender = _senders.TryGetValue(type, out var resolved)
            ? resolved
            : throw new InvalidOperationException(
                $"No report sender is registered for channel type {type}.");
        return sender;
    }

    private async Task<NotificationChannel> FindAsync(Guid channelId, CancellationToken cancellationToken) =>
        await dbContext.NotificationChannels
            .AsNoTracking()
            .SingleOrDefaultAsync(channel => channel.Id == channelId, cancellationToken)
        ?? throw new NotificationChannelNotFoundException(channelId);

    private async Task<NotificationChannel> FindTrackedAsync(
        Guid channelId,
        CancellationToken cancellationToken) =>
        await dbContext.NotificationChannels
            .SingleOrDefaultAsync(channel => channel.Id == channelId, cancellationToken)
        ?? throw new NotificationChannelNotFoundException(channelId);

    private (ChannelSettings Settings, ChannelSecretCiphertexts Secrets) BuildChannelMaterial(
        NotificationChannelType type,
        EmailChannelInput? email,
        string? smtpPassword,
        bool clearPassword,
        string? webhookUrl,
        string? signSecret,
        string? existingPasswordCiphertext,
        string? existingPasswordSuffix,
        string? existingWebhookCiphertext,
        string? existingWebhookSuffix,
        string? existingSignSecretCiphertext,
        string? existingSignSecretSuffix)
    {
        return type switch
        {
            NotificationChannelType.Email when email is not null => BuildEmailMaterial(
                email,
                smtpPassword,
                clearPassword,
                existingPasswordCiphertext,
                existingPasswordSuffix),
            NotificationChannelType.DingTalk or NotificationChannelType.Feishu => BuildWebhookMaterial(
                webhookUrl,
                signSecret,
                existingWebhookCiphertext,
                existingWebhookSuffix,
                existingSignSecretCiphertext,
                existingSignSecretSuffix,
                type),
            _ => throw new ArgumentException(
                "The channel settings do not match the channel type.",
                nameof(email)),
        };
    }

    private (ChannelSettings Settings, ChannelSecretCiphertexts Secrets) BuildEmailMaterial(
        EmailChannelInput input,
        string? smtpPassword,
        bool clearPassword,
        string? existingPasswordCiphertext,
        string? existingPasswordSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Host, nameof(input));
        if (!string.IsNullOrWhiteSpace(smtpPassword) && string.IsNullOrWhiteSpace(input.Username))
        {
            throw new ArgumentException(
                "The SMTP username is required when a password is configured.",
                nameof(input));
        }

        var passwordCiphertext = existingPasswordCiphertext;
        var passwordSuffix = existingPasswordSuffix;
        if (clearPassword)
        {
            passwordCiphertext = null;
            passwordSuffix = null;
        }
        else if (smtpPassword is { } normalized)
        {
            var trimmed = normalized.Trim();
            if (trimmed.Length is < 8 or > 1024)
            {
                throw new ArgumentException(
                    "The SMTP password must contain between 8 and 1024 characters.",
                    nameof(smtpPassword));
            }

            passwordCiphertext = _protector.Protect(trimmed);
            passwordSuffix = ChannelSecretProtector.CreateSuffix(trimmed);
        }

        var settings = new ChannelSettings.Email(
            input.Host.Trim(),
            input.Port,
            input.Security,
            input.Username,
            input.FromAddress,
            input.FromName,
            input.ToAddresses,
            input.CcAddresses);
        return (settings, new ChannelSecretCiphertexts(
            SmtpPasswordCiphertext: passwordCiphertext,
            SmtpPasswordSuffix: passwordSuffix));
    }

    private (ChannelSettings Settings, ChannelSecretCiphertexts Secrets) BuildWebhookMaterial(
        string? webhookUrl,
        string? signSecret,
        string? existingWebhookCiphertext,
        string? existingWebhookSuffix,
        string? existingSignSecretCiphertext,
        string? existingSignSecretSuffix,
        NotificationChannelType type)
    {
        string? urlCiphertext;
        string? urlSuffix;
        if (webhookUrl is { } providedUrl)
        {
            var normalizedUrl = ValidateWebhookUrl(providedUrl, type);
            urlCiphertext = _protector.Protect(normalizedUrl);
            urlSuffix = ChannelSecretProtector.CreateSuffix(normalizedUrl);
        }
        else
        {
            urlCiphertext = existingWebhookCiphertext;
            urlSuffix = existingWebhookSuffix;
        }

        string? secretCiphertext;
        string? secretSuffix;
        if (signSecret is { } providedSecret)
        {
            var normalizedSecret = ValidateSignSecret(providedSecret);
            secretCiphertext = _protector.Protect(normalizedSecret);
            secretSuffix = ChannelSecretProtector.CreateSuffix(normalizedSecret);
        }
        else
        {
            secretCiphertext = existingSignSecretCiphertext;
            secretSuffix = existingSignSecretSuffix;
        }

        if (urlCiphertext is null || secretCiphertext is null)
        {
            throw new ArgumentException(
                "The webhook URL and signing secret are required.",
                nameof(webhookUrl));
        }

        var settings = type == NotificationChannelType.DingTalk
            ? (ChannelSettings)new ChannelSettings.DingTalk()
            : new ChannelSettings.Feishu();
        return (settings, new ChannelSecretCiphertexts(
            WebhookCiphertext: urlCiphertext,
            WebhookSuffix: urlSuffix,
            SignSecretCiphertext: secretCiphertext,
            SignSecretSuffix: secretSuffix));
    }

    private static string ValidateWebhookUrl(string value, NotificationChannelType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.AbsolutePath)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !WebhookHosts.TryGetValue(type, out var allowedHost)
            || !string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The webhook URL must be an HTTPS URL on {WebhookHosts[type]} without credentials or fragment.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidateSignSecret(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = value.Trim();
        return normalized.Length is >= 8 and <= 512
            ? normalized
            : throw new ArgumentException(
                "The webhook signing secret must contain between 8 and 512 characters.",
                nameof(value));
    }

    private static NotificationChannelSummary Map(NotificationChannel channel) => new(
        channel.Id,
        channel.Type,
        channel.Name,
        channel.Enabled,
        channel.Type == NotificationChannelType.Email ? MapEmail(channel) : null,
        channel.Type is NotificationChannelType.DingTalk or NotificationChannelType.Feishu
            ? MapWebhook(channel)
            : null,
        channel.Revision,
        channel.CreatedAt,
        channel.UpdatedAt,
        channel.LastTestedAt,
        channel.LastTestSucceeded,
        channel.LastTestCode);

    private static EmailChannelDisplay MapEmail(NotificationChannel channel)
    {
        if (channel.SmtpHost is null
            || channel.SmtpPort is not { } port
            || channel.SmtpSecurity is not { } security
            || channel.FromAddress is null
            || channel.ToAddressesJson is null)
        {
            throw new InvalidOperationException("The email channel is not fully configured.");
        }

        return new EmailChannelDisplay(
            channel.SmtpHost,
            port,
            security,
            channel.SmtpUsername,
            channel.FromAddress,
            channel.FromName,
            ParseAddresses(channel.ToAddressesJson),
            ParseAddresses(channel.CcAddressesJson) ?? [],
            channel.SmtpPasswordCiphertext is not null,
            channel.SmtpPasswordSuffix is null ? null : $"****{channel.SmtpPasswordSuffix}");
    }

    private static WebhookChannelDisplay MapWebhook(NotificationChannel channel) => new(
        channel.WebhookCiphertext is not null,
        channel.WebhookSuffix is null ? null : $"****{channel.WebhookSuffix}",
        channel.SignSecretSuffix is null ? null : $"****{channel.SignSecretSuffix}");

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
            throw new InvalidOperationException("The stored channel address list is invalid.", exception);
        }
    }
}
