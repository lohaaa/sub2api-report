using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.System;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Revision).IsConcurrencyToken();
        builder.Property(setting => setting.Id).ValueGeneratedNever();
        builder.Property(setting => setting.InitializedAt);
        builder.Property(setting => setting.Timezone).HasMaxLength(100).IsRequired();
        builder.Property(setting => setting.ReleaseChannel).HasMaxLength(32).IsRequired();
        builder.Property(setting => setting.LogLevel).HasMaxLength(16).IsRequired();
        builder.Property(setting => setting.ReportConcurrency).IsRequired();
        builder.Property(setting => setting.ReportRetentionMonths).IsRequired();
        builder.Property(setting => setting.BackupRetentionCount).IsRequired();
        builder.Property(setting => setting.ReportExternalBaseUrl).HasMaxLength(2048);
        builder.Property(setting => setting.ReportDownloadLinkHours)
            .HasDefaultValue(SystemSetting.DefaultReportDownloadLinkHours)
            .IsRequired();
        builder.Property(setting => setting.ReportDownloadMaxDownloads);
        builder.Property(setting => setting.Revision).IsConcurrencyToken().IsRequired();
        builder.Property(setting => setting.UpdatedAt);

        builder.HasData(SystemSetting.CreateDefault());
    }
}
