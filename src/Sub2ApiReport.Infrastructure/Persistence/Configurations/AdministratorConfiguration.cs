using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Infrastructure.Identity;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class AdministratorConfiguration : IEntityTypeConfiguration<Administrator>
{
    public void Configure(EntityTypeBuilder<Administrator> builder)
    {
        builder.ToTable("AdminUsers");
        builder.Property(user => user.SingletonKey).ValueGeneratedNever();
        builder.Property(user => user.CreatedAt).IsRequired();
        builder.HasIndex(user => user.SingletonKey).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AdminUsers_SingletonKey",
            $"SingletonKey = {Administrator.SingletonKeyValue}"));
    }
}
