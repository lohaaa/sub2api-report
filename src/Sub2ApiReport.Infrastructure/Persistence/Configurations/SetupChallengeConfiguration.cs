using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Security;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class SetupChallengeConfiguration : IEntityTypeConfiguration<SetupChallenge>
{
    public void Configure(EntityTypeBuilder<SetupChallenge> builder)
    {
        builder.ToTable("SetupChallenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.CodeHash).IsRequired();
        builder.Property(challenge => challenge.CreatedAt).IsRequired();
        builder.Property(challenge => challenge.ExpiresAt).IsRequired();
        builder.Property(challenge => challenge.FailedAttempts).IsRequired();
        builder.HasIndex(challenge => challenge.ExpiresAt);
    }
}
