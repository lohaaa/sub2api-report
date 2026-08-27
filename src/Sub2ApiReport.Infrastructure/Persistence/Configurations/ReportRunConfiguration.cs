using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        builder.ToTable("ReportRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.IdempotencyKey).HasMaxLength(128);
        builder.HasMany(run => run.Deliveries)
            .WithOne(delivery => delivery.Run)
            .HasForeignKey(delivery => delivery.RunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ReportSnapshot>()
            .WithMany()
            .HasForeignKey(run => run.ReportSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ReportSnapshotId, run.StartedAt });
        builder.HasIndex(run => run.IdempotencyKey).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Trigger",
            "Trigger = 'ManualDelivery'"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Status",
            "Status IN ('Running', 'Succeeded', 'PartialFailed', 'Failed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Completion",
            "(Status = 'Running' AND CompletedAt IS NULL) "
            + "OR (Status <> 'Running' AND CompletedAt IS NOT NULL)"));
    }
}
