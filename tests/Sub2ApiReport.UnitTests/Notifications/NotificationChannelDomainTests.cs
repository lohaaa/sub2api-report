using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.UnitTests.Notifications;

public sealed class NotificationChannelDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly ChannelSettings.Email EmailSettings = new(
        "smtp.example.com",
        587,
        SmtpSecurityMode.StartTls,
        "reports@example.com",
        "reports@example.com",
        "Sub2API Report",
        ["recipient@example.com", "Recipient@example.com"],
        ["cc@example.com"]);

    [Fact]
    public void CreateEmailChannelNormalizesAddressesAndIsolatesColumns()
    {
        var channel = NotificationChannel.Create(
            NotificationChannelType.Email,
            "合成邮件渠道",
            true,
            EmailSettings,
            new ChannelSecretCiphertexts(
                SmtpPasswordCiphertext: "encrypted-password",
                SmtpPasswordSuffix: "ord1"),
            Now);

        Assert.Equal(NotificationChannelType.Email, channel.Type);
        Assert.Equal("合成邮件渠道", channel.Name);
        Assert.True(channel.Enabled);
        Assert.Equal("smtp.example.com", channel.SmtpHost);
        Assert.Equal(587, channel.SmtpPort);
        Assert.Equal(SmtpSecurityMode.StartTls, channel.SmtpSecurity);
        Assert.Equal("""["recipient@example.com"]""", channel.ToAddressesJson);
        Assert.Equal("""["cc@example.com"]""", channel.CcAddressesJson);
        Assert.Equal("encrypted-password", channel.SmtpPasswordCiphertext);
        Assert.Null(channel.WebhookCiphertext);
        Assert.Null(channel.SignSecretCiphertext);
        Assert.Equal(1, channel.Revision);
    }

    [Fact]
    public void CreateEmailChannelRejectsControlCharactersInAddresses()
    {
        var settings = EmailSettings with { FromAddress = "reports@example.com\r\nBcc: victim@example.com" };

        Assert.Throws<ArgumentException>(() => NotificationChannel.Create(
            NotificationChannelType.Email,
            "合成邮件渠道",
            true,
            settings,
            new ChannelSecretCiphertexts(),
            Now));
    }

    [Fact]
    public void CreateEmailChannelRejectsEmptyRecipientList()
    {
        Assert.Throws<ArgumentException>(() => NotificationChannel.Create(
            NotificationChannelType.Email,
            "合成邮件渠道",
            true,
            EmailSettings with { ToAddresses = [] },
            new ChannelSecretCiphertexts(),
            Now));
    }

    [Fact]
    public void UpdateReplacesSecretsAndIncrementsRevision()
    {
        var channel = NotificationChannel.Create(
            NotificationChannelType.DingTalk,
            "合成钉钉渠道",
            true,
            new ChannelSettings.DingTalk(),
            new ChannelSecretCiphertexts(
                WebhookCiphertext: "https-ciphertext-one",
                WebhookSuffix: "one1",
                SignSecretCiphertext: "secret-ciphertext-one",
                SignSecretSuffix: "one2"),
            Now);

        channel.Update(
            "合成钉钉渠道 2",
            false,
            new ChannelSettings.DingTalk(),
            new ChannelSecretCiphertexts(
                WebhookCiphertext: "https-ciphertext-two",
                WebhookSuffix: "two1",
                SignSecretCiphertext: "https-ciphertext-two",
                SignSecretSuffix: "two2"),
            Now.AddMinutes(5));

        Assert.Equal(2, channel.Revision);
        Assert.Equal("合成钉钉渠道 2", channel.Name);
        Assert.False(channel.Enabled);
        Assert.Equal("https-ciphertext-two", channel.WebhookCiphertext);
        Assert.Equal(Now.AddMinutes(5), channel.UpdatedAt);
    }

    [Fact]
    public void CreateRejectsSettingsThatDoNotMatchType()
    {
        Assert.Throws<ArgumentException>(() => NotificationChannel.Create(
            NotificationChannelType.DingTalk,
            "合成钉钉渠道",
            true,
            new ChannelSettings.Feishu(),
            new ChannelSecretCiphertexts(
                WebhookCiphertext: "ciphertext",
                WebhookSuffix: "ok12",
                SignSecretCiphertext: "ciphertext",
                SignSecretSuffix: "ok34"),
            Now));
    }

    [Fact]
    public void RecordTestStoresLatestOutcome()
    {
        var channel = NotificationChannel.Create(
            NotificationChannelType.Feishu,
            "合成飞书渠道",
            true,
            new ChannelSettings.Feishu(),
            new ChannelSecretCiphertexts(
                WebhookCiphertext: "ciphertext",
                WebhookSuffix: "ok12",
                SignSecretCiphertext: "ciphertext",
                SignSecretSuffix: "ok34"),
            Now);

        channel.RecordTest(false, "business_error", Now);

        Assert.False(channel.LastTestSucceeded);
        Assert.Equal("business_error", channel.LastTestCode);
        Assert.Equal(Now, channel.LastTestedAt);
    }
}
