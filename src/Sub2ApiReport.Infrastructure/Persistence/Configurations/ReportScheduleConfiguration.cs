using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("ReportSchedules");
        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();
        builder.Property(schedule => schedule.LocalTime).HasMaxLength(5).IsRequired();
        builder.Property(schedule => schedule.Timezone).HasMaxLength(100).IsRequired();
        builder.Property(schedule => schedule.ShortMonthStrategy)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(schedule => schedule.WindowSpecsJson);
        builder.Property(schedule => schedule.Revision).IsConcurrencyToken().IsRequired();
        builder.HasData(ReportSchedule.CreateDefault());
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSchedules_Singleton",
            "Id = 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSchedules_DayOfMonth",
            "DayOfMonth BETWEEN 1 AND 31"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSchedules_ShortMonthStrategy",
            "ShortMonthStrategy IN ('UseLastDay', 'SkipMonth')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSchedules_LocalTime",
            "length(LocalTime) = 5"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReportSchedules_Revision",
            "Revision > 0"));
    }
}
