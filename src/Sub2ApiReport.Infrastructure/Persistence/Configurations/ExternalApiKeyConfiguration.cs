using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class ExternalApiKeyConfiguration : IEntityTypeConfiguration<ExternalApiKey>
{
    public void Configure(EntityTypeBuilder<ExternalApiKey> builder)
    {
        builder.ToTable("ExternalApiKeys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.NameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(key => key.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(key => key.ExternalId).IsUnique();
        builder.HasIndex(key => new { key.RetiredAt, key.Status, key.ExternalId });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ExternalApiKeys_ExternalId",
            "ExternalId > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ExternalApiKeys_GroupId",
            "GroupId IS NULL OR GroupId > 0"));
    }
}
