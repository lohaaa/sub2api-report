using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ReportDownloadGrantConfiguration : IEntityTypeConfiguration<ReportDownloadGrant>
{
    public void Configure(EntityTypeBuilder<ReportDownloadGrant> builder)
    {
        builder.ToTable("ReportDownloadGrants");
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.TokenHash)
            .HasMaxLength(ReportDownloadGrant.TokenHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(grant => grant.TokenCiphertext)
            .HasMaxLength(ReportDownloadGrant.TokenCiphertextMaxLength)
            .IsRequired();
        builder.Property(grant => grant.LifetimeHours).IsRequired();
        builder.Property(grant => grant.DownloadCount).IsRequired();
        builder.HasOne(grant => grant.Delivery)
            .WithOne(delivery => delivery.DownloadGrant)
            .HasForeignKey<ReportDownloadGrant>(grant => grant.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(grant => grant.ReportSnapshot)
            .WithMany()
            .HasForeignKey(grant => grant.ReportSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(grant => grant.DeliveryId).IsUnique();
        builder.HasIndex(grant => grant.TokenHash).IsUnique();
        builder.HasIndex(grant => new { grant.ReportSnapshotId, grant.CreatedAt });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportDownloadGrants_LifetimeHours",
            "LifetimeHours BETWEEN 1 AND 720"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportDownloadGrants_DownloadCount",
            "DownloadCount >= 0 AND (MaxDownloads IS NULL OR (MaxDownloads > 0 AND DownloadCount <= MaxDownloads))"));
    }
}
