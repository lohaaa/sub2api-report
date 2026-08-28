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
        builder.Property(run => run.Timezone).HasMaxLength(100);
        builder.Property(run => run.WindowSpecsJson);
        builder.Property(run => run.ResolvedWindowsJson);
        builder.Property(run => run.ErrorCode).HasMaxLength(ReportRun.ErrorCodeMaxLength);
        builder.Property(run => run.ErrorMessage).HasMaxLength(ReportRun.ErrorMessageMaxLength);
        builder.Property(run => run.Attempt).HasDefaultValue(1);
        builder.HasMany(run => run.Deliveries)
            .WithOne(delivery => delivery.Run)
            .HasForeignKey(delivery => delivery.RunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ReportSnapshot>()
            .WithMany()
            .HasForeignKey(run => run.ReportSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReportSchedule>()
            .WithMany()
            .HasForeignKey(run => run.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReportRun>()
            .WithMany()
            .HasForeignKey(run => run.RetryOfRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ReportSnapshotId, run.StartedAt });
        builder.HasIndex(run => new { run.ScheduleId, run.StartedAt });
        builder.HasIndex(run => run.RetryOfRunId);
        builder.HasIndex(run => run.IdempotencyKey).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Trigger",
            "Trigger IN ('ManualDelivery', 'Scheduled', 'ManualScheduled', 'Retry')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Status",
            "Status IN ('Running', 'Queued', 'Collecting', 'Rendering', 'Delivering', "
            + "'Succeeded', 'PartialFailed', 'Failed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Completion",
            "(Status IN ('Succeeded', 'PartialFailed', 'Failed') AND CompletedAtUnixMilliseconds IS NOT NULL) "
            + "OR (Status NOT IN ('Succeeded', 'PartialFailed', 'Failed') "
            + "AND CompletedAtUnixMilliseconds IS NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Attempt",
            "Attempt > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_ScheduleMetadata",
            "Trigger = 'ManualDelivery' OR (ScheduleId IS NOT NULL AND ScheduleRevision > 0 "
            + "AND PeriodEnd IS NOT NULL AND Timezone IS NOT NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportRuns_Idempotency",
            "Trigger <> 'Scheduled' OR IdempotencyKey IS NOT NULL"));
    }
}
