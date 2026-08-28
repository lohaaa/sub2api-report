using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class UnixTimeMigrationStateConfiguration
    : IEntityTypeConfiguration<UnixTimeMigrationState>
{
    public void Configure(EntityTypeBuilder<UnixTimeMigrationState> builder)
    {
        builder.ToTable("UnixTimeMigrationState");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.HasData(UnixTimeMigrationState.CreateCompleted());
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_UnixTimeMigrationState_Completed",
            "Completed = 1"));
    }
}
