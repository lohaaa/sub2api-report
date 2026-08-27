using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class Sub2ApiUserConfiguration : IEntityTypeConfiguration<Sub2ApiUser>
{
    public void Configure(EntityTypeBuilder<Sub2ApiUser> builder)
    {
        builder.ToTable("Sub2ApiUsers");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.EmailSnapshot).HasMaxLength(320).IsRequired();
        builder.Property(user => user.UsernameSnapshot).HasMaxLength(200);
        builder.Property(user => user.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(user => user.ExternalId).IsUnique();
        builder.HasIndex(user => new { user.RetiredAt, user.Status, user.IsSelected });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Sub2ApiUsers_ExternalId",
            "ExternalId > 0"));
    }
}
