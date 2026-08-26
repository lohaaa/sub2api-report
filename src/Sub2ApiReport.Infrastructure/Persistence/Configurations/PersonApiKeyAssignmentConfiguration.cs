using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.People;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class PersonApiKeyAssignmentConfiguration : IEntityTypeConfiguration<PersonApiKeyAssignment>
{
    public void Configure(EntityTypeBuilder<PersonApiKeyAssignment> builder)
    {
        builder.ToTable("PersonApiKeyAssignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Revision).IsConcurrencyToken();
        builder.HasOne(assignment => assignment.Person)
            .WithMany()
            .HasForeignKey(assignment => assignment.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(assignment => assignment.ExternalApiKey)
            .WithMany()
            .HasForeignKey(assignment => assignment.ExternalApiKeyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => new
        {
            assignment.ExternalApiKeyId,
            assignment.ValidFrom,
            assignment.ValidTo,
        });
        builder.HasIndex(assignment => new
        {
            assignment.PersonId,
            assignment.ValidFrom,
            assignment.ValidTo,
        });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PersonApiKeyAssignments_DateRange",
            "ValidTo IS NULL OR ValidTo >= ValidFrom"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PersonApiKeyAssignments_Revision",
            "Revision > 0"));
    }
}
