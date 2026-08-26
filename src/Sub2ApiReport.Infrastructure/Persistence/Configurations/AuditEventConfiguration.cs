using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Audit;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.Actor).HasMaxLength(256);
        builder.Property(auditEvent => auditEvent.Action).HasMaxLength(100).IsRequired();
        builder.Property(auditEvent => auditEvent.Target).HasMaxLength(200).IsRequired();
        builder.Property(auditEvent => auditEvent.Result).HasMaxLength(32).IsRequired();
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(100);
        builder.Property(auditEvent => auditEvent.MetadataJson).HasMaxLength(4000);
        builder.HasIndex(auditEvent => auditEvent.OccurredAt);
    }
}
