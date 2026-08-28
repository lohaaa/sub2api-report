using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ReportGenerationRunConfiguration : IEntityTypeConfiguration<ReportGenerationRun>
{
    public void Configure(EntityTypeBuilder<ReportGenerationRun> builder)
    {
        builder.ToTable("ReportGenerationRuns", table => table.HasCheckConstraint(
            "CK_ReportGenerationRuns_Status",
            "Status IN ('Running', 'Succeeded', 'Failed')"));
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.Stage)
            .HasMaxLength(32);
        builder.Property(item => item.ErrorCode)
            .HasMaxLength(64);
        builder.Property(item => item.ErrorMessage)
            .HasMaxLength(512);
        builder.HasOne<ReportRun>()
            .WithMany()
            .HasForeignKey(item => item.ReportRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.StartedAt, item.Id });
        builder.HasIndex(item => item.Status);
        builder.HasIndex(item => item.ReportRunId).IsUnique();
    }
}
