using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryRecordConfiguration : IEntityTypeConfiguration<DeliveryRecord>
{
    public void Configure(EntityTypeBuilder<DeliveryRecord> builder)
    {
        builder.ToTable("DeliveryRecords");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.ChannelType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(delivery => delivery.ChannelName).HasMaxLength(100).IsRequired();
        builder.Property(delivery => delivery.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(delivery => delivery.PayloadHash).HasMaxLength(DeliveryRecord.PayloadHashLength);
        builder.Property(delivery => delivery.ErrorCode).HasMaxLength(DeliveryRecord.ErrorCodeMaxLength);
        builder.Property(delivery => delivery.ErrorMessage).HasMaxLength(DeliveryRecord.ErrorMessageMaxLength);
        builder.HasOne<NotificationChannel>()
            .WithMany()
            .HasForeignKey(delivery => delivery.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(delivery => new { delivery.RunId, delivery.ChannelId }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryRecords_ChannelType",
            "ChannelType IN ('Email', 'DingTalk', 'Feishu')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryRecords_Status",
            "Status IN ('Pending', 'Sending', 'Succeeded', 'Failed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryRecords_Attempts",
            "Attempts >= 0"));
    }
}
