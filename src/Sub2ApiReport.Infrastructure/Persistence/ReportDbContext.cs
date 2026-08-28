using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SQLite;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Domain.Audit;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
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

    public DbSet<Sub2ApiUser> Sub2ApiUsers => Set<Sub2ApiUser>();

    public DbSet<ExternalApiKey> ExternalApiKeys => Set<ExternalApiKey>();

    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();

    public DbSet<ReportGenerationRun> ReportGenerationRuns => Set<ReportGenerationRun>();

    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();

    public DbSet<ReportRun> ReportRuns => Set<ReportRun>();

    public DbSet<DeliveryRecord> DeliveryRecords => Set<DeliveryRecord>();

    public DbSet<DeliveryPart> DeliveryParts => Set<DeliveryPart>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UnixMillisecondsDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableUnixMillisecondsDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.AddQuartz(quartz => quartz.UseSqlite());
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("AdminUserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("AdminUserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("AdminUserTokens");
        builder.ApplyConfigurationsFromAssembly(typeof(ReportDbContext).Assembly);

        foreach (var property in builder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetColumnName($"{property.Name}UnixMilliseconds");
        }
    }
}
