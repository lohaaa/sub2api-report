using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sub2ApiReport.Domain.Security;
using Sub2ApiReport.Infrastructure.Identity;

namespace Sub2ApiReport.Infrastructure.Persistence.Configurations;

internal sealed class RecoveryChallengeConfiguration : IEntityTypeConfiguration<RecoveryChallenge>
{
    public void Configure(EntityTypeBuilder<RecoveryChallenge> builder)
    {
        builder.ToTable("RecoveryChallenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.CodeHash).IsRequired();
        builder.Property(challenge => challenge.CreatedAt).IsRequired();
        builder.Property(challenge => challenge.ExpiresAt).IsRequired();
        builder.Property(challenge => challenge.FailedAttempts).IsRequired();
        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(challenge => challenge.AdministratorId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(challenge => new { challenge.AdministratorId, challenge.ExpiresAt });
    }
}
