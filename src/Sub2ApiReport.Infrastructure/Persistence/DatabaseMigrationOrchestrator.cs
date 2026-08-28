using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sub2ApiReport.Infrastructure.Persistence;

public static class DatabaseMigrationOrchestrator
{
    public const string LegacySchedulingMigration = "20260827135840_AddReportScheduling";
    public const string SchedulingMigration = "20260828022428_AddReportScheduling";
    public const string UnixTimestampBackfillBoundary =
        "20260828023826_AddUnixTimeMigrationGuard";
    public const string UnixTimestampFinalMigration =
        "20260828025413_CompleteUnixTimeStorage";

    public static async Task MigrateAsync(
        ReportDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await LegacySchedulingMigrationAlias.ApplyAsync(
            dbContext,
            LegacySchedulingMigration,
            SchedulingMigration,
            cancellationToken);
        var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var migrator = dbContext.GetService<IMigrator>();
        if (!appliedMigrations.Contains(UnixTimestampFinalMigration))
        {
            await migrator.MigrateAsync(UnixTimestampBackfillBoundary, cancellationToken);
            await UnixTimestampBackfill.ApplyAsync(dbContext, cancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: cancellationToken);
    }
}
