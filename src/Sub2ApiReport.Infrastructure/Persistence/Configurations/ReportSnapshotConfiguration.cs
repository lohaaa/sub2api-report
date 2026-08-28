using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ReportSnapshotConfiguration : IEntityTypeConfiguration<ReportSnapshot>
{
    public void Configure(EntityTypeBuilder<ReportSnapshot> builder)
    {
        builder.ToTable("ReportSnapshots");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(report => report.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(report => report.Timezone).HasMaxLength(100).IsRequired();
        builder.Property(report => report.SevenDayActualCost).HasPrecision(38, 18);
        builder.Property(report => report.ThirtyDayActualCost).HasPrecision(38, 18);
        builder.Property(report => report.WindowSummaryJson);
        builder.Property(report => report.CanonicalJson).IsRequired();
        builder.HasIndex(report => new { report.GeneratedAt, report.Id });
        builder.HasIndex(report => new { report.CutoffDate, report.Status });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSnapshots_SchemaVersion",
            "SchemaVersion > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSnapshots_ConnectionRevision",
            "ConnectionRevision > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSnapshots_Counts",
            "UserCount >= 0 AND KeyCount >= 0 AND FailedRangeCount >= 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSnapshots_Costs",
            "SevenDayActualCost >= 0 AND ThirtyDayActualCost >= 0"));
    }
}
