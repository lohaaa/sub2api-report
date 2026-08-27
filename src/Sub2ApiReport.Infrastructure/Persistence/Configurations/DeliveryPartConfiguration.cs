using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryPartConfiguration : IEntityTypeConfiguration<DeliveryPart>
{
    public void Configure(EntityTypeBuilder<DeliveryPart> builder)
    {
        builder.ToTable("DeliveryParts");
        builder.HasKey(part => part.Id);
        builder.Property(part => part.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(part => part.PayloadHash).HasMaxLength(DeliveryPart.PayloadHashLength).IsRequired();
        builder.Property(part => part.ErrorCode).HasMaxLength(DeliveryPart.ErrorCodeMaxLength);
        builder.Property(part => part.ErrorMessage).HasMaxLength(DeliveryPart.ErrorMessageMaxLength);
        builder.HasOne(part => part.Delivery)
            .WithMany(delivery => delivery.Parts)
            .HasForeignKey(part => part.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(part => new { part.DeliveryId, part.PartIndex }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryParts_Status",
            "Status IN ('Pending', 'Succeeded', 'Failed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryParts_Attempts",
            "Attempts >= 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeliveryParts_Index",
            "PartIndex >= 0 AND PartCount >= 1 AND PartIndex < PartCount"));
    }
}
