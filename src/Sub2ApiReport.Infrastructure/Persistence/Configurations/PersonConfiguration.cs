using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.People;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Code).HasMaxLength(64).UseCollation("NOCASE").IsRequired();
        builder.Property(person => person.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(person => person.Revision).IsConcurrencyToken();
        builder.HasIndex(person => person.Code).IsUnique();
        builder.HasIndex(person => new { person.IsActive, person.DisplayName });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_People_Revision",
            "Revision > 0"));
    }
}
