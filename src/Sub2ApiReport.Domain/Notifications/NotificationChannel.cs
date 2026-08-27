using System.Text.Json;

namespace Sub2ApiReport.Domain.Notifications;

public enum NotificationChannelType
{
    Email,
    DingTalk,
    Feishu,
}

public enum SmtpSecurityMode
{
    StartTls,
    ImplicitTls,
    None,
}

/// <summary>Non-secret delivery settings of one notification channel.</summary>
public abstract record ChannelSettings
{
    public sealed record Email(
        string Host,
        int Port,
        SmtpSecurityMode Security,
        string? Username,
        string FromAddress,
        string? FromName,
        IReadOnlyList<string> ToAddresses,
        IReadOnlyList<string> CcAddresses) : ChannelSettings;

    public sealed record DingTalk() : ChannelSettings;

    public sealed record Feishu() : ChannelSettings;
}

/// <summary>Data Protection ciphertexts and masks of one channel's secrets.</summary>
public sealed record ChannelSecretCiphertexts(
    string? SmtpPasswordCiphertext = null,
    string? SmtpPasswordSuffix = null,
    string? WebhookCiphertext = null,
    string? WebhookSuffix = null,
    string? SignSecretCiphertext = null,
    string? SignSecretSuffix = null);

public sealed class NotificationChannel
{
    public const int NameMaxLength = 100;
    private const int HostMaxLength = 255;
    private const int AddressMaxLength = 320;
    private const int FromNameMaxLength = 200;
    private const int AddressListMaxLength = 4096;
    private const int CiphertextMaxLength = 16384;
    private const int SuffixMaxLength = 8;

    private static readonly JsonSerializerOptions AddressListSerializerOptions = new();

    private NotificationChannel()
    {
    }

    public Guid Id { get; private init; }

    public NotificationChannelType Type { get; private init; }

    public string Name { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public string? SmtpHost { get; private set; }

    public int? SmtpPort { get; private set; }

    public SmtpSecurityMode? SmtpSecurity { get; private set; }

    public string? SmtpUsername { get; private set; }

    public string? FromAddress { get; private set; }

    public string? FromName { get; private set; }

    public string? ToAddressesJson { get; private set; }

    public string? CcAddressesJson { get; private set; }

    public string? SmtpPasswordCiphertext { get; private set; }

    public string? SmtpPasswordSuffix { get; private set; }

    public string? WebhookCiphertext { get; private set; }

    public string? WebhookSuffix { get; private set; }

    public string? SignSecretCiphertext { get; private set; }

    public string? SignSecretSuffix { get; private set; }

    public long Revision { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastTestedAt { get; private set; }

    public bool? LastTestSucceeded { get; private set; }

    public string? LastTestCode { get; private set; }

    public static NotificationChannel Create(
        NotificationChannelType type,
        string name,
        bool enabled,
        ChannelSettings settings,
        ChannelSecretCiphertexts secrets,
        DateTimeOffset createdAt)
    {
        if (type is not (NotificationChannelType.Email or NotificationChannelType.DingTalk
            or NotificationChannelType.Feishu))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "The channel type is not supported.");
        }

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = type,
            CreatedAt = createdAt,
        };
        channel.Apply(name, enabled, settings, secrets, createdAt);
        return channel;
    }

    public void Update(
        string name,
        bool enabled,
        ChannelSettings settings,
        ChannelSecretCiphertexts secrets,
        DateTimeOffset updatedAt)
    {
        Apply(name, enabled, settings, secrets, updatedAt);
        Revision++;
        UpdatedAt = updatedAt;
    }

    public void RecordTest(bool succeeded, string code, DateTimeOffset testedAt)
    {
        LastTestSucceeded = succeeded;
        LastTestCode = ValidateText(code, 64, nameof(code));
        LastTestedAt = testedAt;
    }

    private void Apply(
        string name,
        bool enabled,
        ChannelSettings settings,
        ChannelSecretCiphertexts secrets,
        DateTimeOffset updatedAt)
    {
        Name = ValidateText(name, NameMaxLength, nameof(name));
        Enabled = enabled;
        UpdatedAt = updatedAt;
        switch (Type, settings)
        {
            case (NotificationChannelType.Email, ChannelSettings.Email email):
                ApplyEmail(email, secrets);
                break;
            case (NotificationChannelType.DingTalk, ChannelSettings.DingTalk):
                ApplyWebhook(secrets);
                break;
            case (NotificationChannelType.Feishu, ChannelSettings.Feishu):
                ApplyWebhook(secrets);
                break;
            default:
                throw new ArgumentException(
                    "The channel settings do not match the channel type.",
                    nameof(settings));
        }
    }

    private void ApplyEmail(ChannelSettings.Email settings, ChannelSecretCiphertexts secrets)
    {
        SmtpHost = ValidateText(settings.Host, HostMaxLength, nameof(settings));
        SmtpPort = settings.Port is >= 1 and <= 65535
            ? settings.Port
            : throw new ArgumentException("The SMTP port must be between 1 and 65535.");
        SmtpSecurity = settings.Security;
        SmtpUsername = string.IsNullOrWhiteSpace(settings.Username)
            ? null
            : ValidateText(settings.Username, 320, nameof(settings));
        FromAddress = ValidateAddress(settings.FromAddress, nameof(settings));
        FromName = string.IsNullOrWhiteSpace(settings.FromName)
            ? null
            : ValidateText(settings.FromName, FromNameMaxLength, nameof(settings));
        ToAddressesJson = SerializeAddresses(settings.ToAddresses, requireAny: true, nameof(settings));
        CcAddressesJson = settings.CcAddresses is { Count: > 0 }
            ? SerializeAddresses(settings.CcAddresses, requireAny: false, nameof(settings))
            : null;
        SmtpPasswordCiphertext = ValidateCiphertext(
            secrets.SmtpPasswordCiphertext,
            CiphertextMaxLength,
            nameof(secrets));
        SmtpPasswordSuffix = ValidateSuffix(secrets.SmtpPasswordSuffix);

        WebhookCiphertext = null;
        WebhookSuffix = null;
        SignSecretCiphertext = null;
        SignSecretSuffix = null;
    }

    private void ApplyWebhook(ChannelSecretCiphertexts secrets)
    {
        SmtpHost = null;
        SmtpPort = null;
        SmtpSecurity = null;
        SmtpUsername = null;
        FromAddress = null;
        FromName = null;
        ToAddressesJson = null;
        CcAddressesJson = null;
        SmtpPasswordCiphertext = null;
        SmtpPasswordSuffix = null;

        WebhookCiphertext = ValidateCiphertext(secrets.WebhookCiphertext, 4096, nameof(secrets));
        WebhookSuffix = ValidateSuffix(secrets.WebhookSuffix);
        SignSecretCiphertext = ValidateCiphertext(secrets.SignSecretCiphertext, CiphertextMaxLength, nameof(secrets));
        SignSecretSuffix = ValidateSuffix(secrets.SignSecretSuffix);
    }

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? ValidateCiphertext(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized is { Length: > 0 } && normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                "The encrypted secret must not be empty and cannot exceed its maximum length.",
                parameterName);
    }

    private static string? ValidateSuffix(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= SuffixMaxLength
            ? normalized
            : throw new ArgumentException(
                "The secret suffix cannot exceed 8 characters.",
                nameof(value));
    }

    private static string ValidateAddress(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var address = value.Trim();
        var atIndex = address.IndexOf('@');
        if (address.Length > 320
            || atIndex <= 0
            || atIndex == address.Length - 1
            || address.IndexOf('@', atIndex + 1) >= 0
            || ContainsForbiddenCharacter(address))
        {
            throw new ArgumentException(
                "The email address must not contain whitespace or separator characters.",
                parameterName);
        }

        return address;
    }

    private static bool ContainsForbiddenCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '"' or '<' or '>' or ',')
            {
                return true;
            }
        }

        return false;
    }

    private static string SerializeAddresses(
        IReadOnlyList<string> addresses,
        bool requireAny,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(addresses, parameterName);
        var normalized = addresses
            .Select(address => ValidateAddress(address, parameterName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requireAny && normalized.Length == 0)
        {
            throw new ArgumentException("At least one recipient address is required.", parameterName);
        }

        var json = JsonSerializer.Serialize(normalized, AddressListSerializerOptions);
        return json.Length <= AddressListMaxLength
            ? json
            : throw new ArgumentException("The address list cannot exceed its maximum length.", parameterName);
    }
}
