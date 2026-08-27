using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Notifications;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder)
    {
        builder.ToTable("NotificationChannels");
        builder.HasKey(channel => channel.Id);
        builder.Property(channel => channel.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(channel => channel.Name).HasMaxLength(NotificationChannel.NameMaxLength).IsRequired();
        builder.Property(channel => channel.SmtpHost).HasMaxLength(255);
        builder.Property(channel => channel.SmtpSecurity).HasConversion<string>().HasMaxLength(16);
        builder.Property(channel => channel.SmtpUsername).HasMaxLength(320);
        builder.Property(channel => channel.FromAddress).HasMaxLength(320);
        builder.Property(channel => channel.FromName).HasMaxLength(200);
        builder.Property(channel => channel.ToAddressesJson).HasMaxLength(4096);
        builder.Property(channel => channel.CcAddressesJson).HasMaxLength(4096);
        builder.Property(channel => channel.SmtpPasswordCiphertext).HasMaxLength(16384);
        builder.Property(channel => channel.SmtpPasswordSuffix).HasMaxLength(8);
        builder.Property(channel => channel.WebhookCiphertext).HasMaxLength(4096);
        builder.Property(channel => channel.WebhookSuffix).HasMaxLength(8);
        builder.Property(channel => channel.SignSecretCiphertext).HasMaxLength(16384);
        builder.Property(channel => channel.SignSecretSuffix).HasMaxLength(8);
        builder.Property(channel => channel.LastTestCode).HasMaxLength(64);
        builder.Property(channel => channel.Revision).IsConcurrencyToken();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_Name",
            "length(Name) >= 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_Revision",
            "Revision > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_EmailFields",
            "Type <> 'Email' OR (SmtpHost IS NOT NULL AND SmtpPort IS NOT NULL AND SmtpSecurity IS NOT NULL "
            + "AND FromAddress IS NOT NULL AND ToAddressesJson IS NOT NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_EmailSecret",
            "Type <> 'Email' OR (SmtpPasswordCiphertext IS NULL AND SmtpPasswordSuffix IS NULL) "
            + "OR (SmtpPasswordCiphertext IS NOT NULL AND SmtpPasswordSuffix IS NOT NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_WebhookFields",
            "Type NOT IN ('DingTalk', 'Feishu') OR (WebhookCiphertext IS NOT NULL AND WebhookSuffix IS NOT NULL "
            + "AND SignSecretCiphertext IS NOT NULL AND SignSecretSuffix IS NOT NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_EmailExclusive",
            "Type = 'Email' OR (SmtpHost IS NULL AND SmtpPort IS NULL AND SmtpSecurity IS NULL "
            + "AND SmtpUsername IS NULL AND FromAddress IS NULL AND FromName IS NULL AND ToAddressesJson IS NULL "
            + "AND CcAddressesJson IS NULL AND SmtpPasswordCiphertext IS NULL AND SmtpPasswordSuffix IS NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationChannels_WebhookExclusive",
            "Type IN ('DingTalk', 'Feishu') OR (WebhookCiphertext IS NULL AND WebhookSuffix IS NULL "
            + "AND SignSecretCiphertext IS NULL AND SignSecretSuffix IS NULL)"));
    }
}
