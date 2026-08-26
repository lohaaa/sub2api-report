using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class Sub2ApiConnectionConfiguration : IEntityTypeConfiguration<Sub2ApiConnection>
{
    public void Configure(EntityTypeBuilder<Sub2ApiConnection> builder)
    {
        builder.ToTable("Sub2ApiConnections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.BaseUrl).HasMaxLength(2048).IsRequired();
        builder.Property(connection => connection.AdminApiKeyCiphertext).HasMaxLength(16384);
        builder.Property(connection => connection.AdminApiKeySuffix).HasMaxLength(8);
        builder.Property(connection => connection.LastTestCode).HasMaxLength(64);
        builder.Property(connection => connection.Revision).IsConcurrencyToken();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Sub2ApiConnections_Singleton",
            $"Id = {Sub2ApiConnection.SingletonId}"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Sub2ApiConnections_UserId",
            "UserId > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Sub2ApiConnections_CodexGroupId",
            "CodexGroupId IS NULL OR CodexGroupId > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Sub2ApiConnections_Revision",
            "Revision > 0"));
    }
}
