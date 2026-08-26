using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Domain.Audit;
using Sub2ApiReport.Domain.People;
using Sub2ApiReport.Domain.Security;
using Sub2ApiReport.Domain.Sub2Api;
using Sub2ApiReport.Domain.System;
using Sub2ApiReport.Infrastructure.Identity;

namespace Sub2ApiReport.Infrastructure.Persistence;

public sealed class ReportDbContext(DbContextOptions<ReportDbContext> options)
    : IdentityUserContext<Administrator, Guid>(options)
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<SetupChallenge> SetupChallenges => Set<SetupChallenge>();

    public DbSet<RecoveryChallenge> RecoveryChallenges => Set<RecoveryChallenge>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Sub2ApiConnection> Sub2ApiConnections => Set<Sub2ApiConnection>();

    public DbSet<ExternalApiKey> ExternalApiKeys => Set<ExternalApiKey>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<PersonApiKeyAssignment> PersonApiKeyAssignments => Set<PersonApiKeyAssignment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("AdminUserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("AdminUserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("AdminUserTokens");
        builder.ApplyConfigurationsFromAssembly(typeof(ReportDbContext).Assembly);
    }
}
