using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task InitialMigrationCreatesSystemSettingsSingleton()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ReportDbContext(options);
        await MigrateWithUnixBackfillAsync(dbContext);
        var setting = await dbContext.SystemSettings.SingleAsync(CancellationToken.None);

        Assert.Equal(1, setting.Id);
        Assert.Equal("Asia/Shanghai", setting.Timezone);
        Assert.Equal("stable", setting.ReleaseChannel);
        Assert.Equal("Information", setting.LogLevel);
        Assert.Equal(4, setting.ReportConcurrency);
        Assert.Equal(12, setting.ReportRetentionMonths);
        Assert.Equal(10, setting.BackupRetentionCount);
        Assert.Null(setting.ReportExternalBaseUrl);
        Assert.Equal(24, setting.ReportDownloadLinkHours);
        Assert.Null(setting.ReportDownloadMaxDownloads);
        Assert.Empty(await dbContext.ReportDownloadGrants.ToListAsync(CancellationToken.None));
        Assert.Equal(1, setting.Revision);

        var schedule = await dbContext.ReportSchedules.SingleAsync(CancellationToken.None);
        Assert.False(schedule.Enabled);
        Assert.Equal(1, schedule.DayOfMonth);
        Assert.Equal(ShortMonthStrategy.UseLastDay, schedule.ShortMonthStrategy);
        Assert.Equal("09:00", schedule.LocalTime);
        Assert.Equal("Asia/Shanghai", schedule.Timezone);

        var quartzTables = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name LIKE 'QRTZ_%'")
            .ToListAsync(CancellationToken.None);
        Assert.Contains("QRTZ_JOB_DETAILS", quartzTables);
        Assert.Contains("QRTZ_TRIGGERS", quartzTables);
        Assert.Contains("QRTZ_FIRED_TRIGGERS", quartzTables);

        var timestampProperties = dbContext.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset?))
            .ToArray();
        Assert.NotEmpty(timestampProperties);
        Assert.All(timestampProperties, property =>
        {
            var providerType = property.GetTypeMapping().Converter?.ProviderClrType;
            Assert.Equal(typeof(long), Nullable.GetUnderlyingType(providerType ?? typeof(void)) ?? providerType);
            Assert.Equal("INTEGER", property.GetColumnType());
            Assert.EndsWith("UnixMilliseconds", property.GetColumnName(), StringComparison.Ordinal);
        });

        var channelColumns = await GetColumnTypesAsync(dbContext, "NotificationChannels");
        Assert.DoesNotContain("CreatedAt", channelColumns.Keys);
        Assert.DoesNotContain("LastTestedAt", channelColumns.Keys);
        Assert.Equal("INTEGER", channelColumns["CreatedAtUnixMilliseconds"]);
        Assert.Equal("INTEGER", channelColumns["LastTestedAtUnixMilliseconds"]);
    }

    [Fact]
    public async Task ShortMonthStrategyMigrationExtendsDayRangeAndSeedsDefaultStrategy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(SchedulingMigration, CancellationToken.None);

        // The pre-migration schema still restricts the configured day to 1..28.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReportSchedules SET DayOfMonth = 31 WHERE Id = 1",
                CancellationToken.None));
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ReportSchedules SET DayOfMonth = 21 WHERE Id = 1",
            CancellationToken.None);

        await MigrateWithUnixBackfillAsync(dbContext);

        var seededStrategy = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT ShortMonthStrategy AS Value FROM ReportSchedules WHERE Id = 1")
            .SingleAsync(CancellationToken.None);
        Assert.Equal("UseLastDay", seededStrategy);

        // The migrated schema preserves the configured day and allows day 31.
        dbContext.ChangeTracker.Clear();
        var schedule = await dbContext.ReportSchedules
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(21, schedule.DayOfMonth);
        Assert.Equal(ShortMonthStrategy.UseLastDay, schedule.ShortMonthStrategy);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ReportSchedules SET DayOfMonth = 31 WHERE Id = 1",
            CancellationToken.None);

        // Out-of-range days and unknown strategies stay rejected by the new checks.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReportSchedules SET DayOfMonth = 32 WHERE Id = 1",
                CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReportSchedules SET DayOfMonth = 0 WHERE Id = 1",
                CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReportSchedules SET ShortMonthStrategy = 'DeferMonth' WHERE Id = 1",
                CancellationToken.None));
        var tableSql = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'ReportSchedules'")
            .SingleAsync(CancellationToken.None);
        Assert.Contains("CK_ReportSchedules_DayOfMonth", tableSql, StringComparison.Ordinal);
        Assert.Contains("CK_ReportSchedules_ShortMonthStrategy", tableSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacySchedulingMigrationHistoryIsAliasedAfterSchemaVerification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(SchedulingMigration, CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE __EFMigrationsHistory
            SET MigrationId = {LegacySchedulingMigration}
            WHERE MigrationId = {SchedulingMigration}
            """, CancellationToken.None);

        await LegacySchedulingMigrationAlias.ApplyAsync(
            dbContext,
            LegacySchedulingMigration,
            SchedulingMigration,
            CancellationToken.None);

        var applied = await dbContext.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains(SchedulingMigration, applied);
        Assert.DoesNotContain(LegacySchedulingMigration, applied);
        await MigrateWithUnixBackfillAsync(dbContext);
    }

    [Fact]
    public async Task InvalidLegacyTimestampRollsBackBackfillAndBlocksFinalMigration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20260826100503_AddDynamicSystemSettings",
            CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE SystemSettings SET UpdatedAt = 'invalid-time' WHERE Id = 1",
            CancellationToken.None);
        await migrator.MigrateAsync(UnixTimestampBackfillBoundary, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UnixTimestampBackfill.ApplyAsync(dbContext, CancellationToken.None));

        Assert.Contains("SystemSettings.UpdatedAt", exception.Message, StringComparison.Ordinal);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.DoesNotContain(UnixTimestampPhaseTwo, applied);
        var columns = await GetColumnTypesAsync(dbContext, "SystemSettings");
        Assert.Equal("TEXT", columns["UpdatedAt"]);
        Assert.Equal("INTEGER", columns["UpdatedAtUnixMilliseconds"]);
        var unixValue = await dbContext.Database
            .SqlQueryRaw<long?>(
                "SELECT UpdatedAtUnixMilliseconds AS Value FROM SystemSettings WHERE Id = 1")
            .SingleAsync(CancellationToken.None);
        Assert.Null(unixValue);
    }

    [Fact]
    public async Task DirectEfMigrationCannotBypassUnixTimestampBackfill()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();

        _ = await Assert.ThrowsAnyAsync<Exception>(() =>
            migrator.MigrateAsync(cancellationToken: CancellationToken.None));

        var applied = await dbContext.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains(UnixTimestampBackfillBoundary, applied);
        Assert.DoesNotContain(UnixTimestampValidation, applied);
        Assert.DoesNotContain(UnixTimestampPhaseTwo, applied);
        var columns = await GetColumnTypesAsync(dbContext, "NotificationChannels");
        Assert.Equal("TEXT", columns["CreatedAt"]);
        Assert.Equal("INTEGER", columns["CreatedAtUnixMilliseconds"]);
    }

    [Fact]
    public async Task M3MigrationPreservesInitializedAdministratorAndSettings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync("20260826103316_AddIdentityAndSetup", CancellationToken.None);
        var initializedAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SystemSettings SET InitializedAt = {initializedAt}, UpdatedAt = {initializedAt} WHERE Id = 1",
            CancellationToken.None);
        var administratorId = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO AdminUsers
                (Id, SingletonKey, CreatedAt, UserName, NormalizedUserName,
                 EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
                 LockoutEnabled, AccessFailedCount)
            VALUES
                ({administratorId}, 1, {initializedAt}, 'synthetic-admin', 'SYNTHETIC-ADMIN',
                 0, 0, 0, 1, 0)
            """, CancellationToken.None);

        await MigrateWithUnixBackfillAsync(dbContext);

        Assert.Single(await dbContext.Users.AsNoTracking().ToListAsync(CancellationToken.None));
        var migratedSetting = await dbContext.SystemSettings.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(initializedAt, migratedSetting.InitializedAt);
        Assert.Equal(4, migratedSetting.ReportConcurrency);
        Assert.Empty(await dbContext.Sub2ApiConnections.AsNoTracking().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.ExternalApiKeys.AsNoTracking().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.ReportSnapshots.AsNoTracking().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task M6MigrationPreservesExistingManualDeliveryRuns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ReportDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20260827104047_RemovePeopleAndAddReportGenerationRuns",
            CancellationToken.None);
        var snapshotId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(
            2026,
            8,
            27,
            10,
            0,
            0,
            TimeSpan.FromHours(5.5)).AddMilliseconds(123);
        const string canonicalJson = "{}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ReportSnapshots
                (Id, SchemaVersion, Status, Trigger, CutoffDate, Timezone,
                 ConnectionRevision, GeneratedAt, GeneratedAtUnixMilliseconds,
                 FailedRangeCount, KeyCount, UserCount, SevenDayActualCost,
                 ThirtyDayActualCost, CanonicalJson)
            VALUES
                ({snapshotId}, 3, 'Complete', 'ManualDryRun', '2026-08-26', 'UTC',
                 1, {timestamp}, {timestamp.ToUnixTimeMilliseconds()},
                 0, 1, 1, 0, 0, {canonicalJson})
            """, CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ReportRuns
                (Id, ReportSnapshotId, Trigger, Status, StartedAt, CompletedAt, IdempotencyKey)
            VALUES
                ({runId}, {snapshotId}, 'ManualDelivery', 'Succeeded', {timestamp}, {timestamp}, NULL)
            """, CancellationToken.None);

        await MigrateWithUnixBackfillAsync(dbContext);
        dbContext.ChangeTracker.Clear();

        var migratedRun = await dbContext.ReportRuns
            .AsNoTracking()
            .SingleAsync(item => item.Id == runId, CancellationToken.None);
        Assert.Equal(ReportRunTrigger.ManualDelivery, migratedRun.Trigger);
        Assert.Equal(ReportRunStatus.Succeeded, migratedRun.Status);
        Assert.Equal(1, migratedRun.Attempt);
        Assert.Equal(timestamp.ToUniversalTime(), migratedRun.StartedAt);
        Assert.Equal(timestamp.ToUniversalTime(), migratedRun.CompletedAt);
        Assert.Equal(snapshotId, migratedRun.ReportSnapshotId);
    }

    [Fact]
    public async Task DatabaseSettingsCanChangeWithoutRestartingTheServiceProvider()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sub2api-report-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddInfrastructure($"Data Source={databasePath}");

        try
        {
            await using var serviceProvider = services.BuildServiceProvider();

            await using (var migrationScope = serviceProvider.CreateAsyncScope())
            {
                var dbContext = migrationScope.ServiceProvider.GetRequiredService<ReportDbContext>();
                await MigrateWithUnixBackfillAsync(dbContext);
            }

            long originalRevision;
            await using (var updateScope = serviceProvider.CreateAsyncScope())
            {
                var settingsService = updateScope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
                var current = await settingsService.GetAsync(CancellationToken.None);
                originalRevision = current.Revision;

                var updated = await settingsService.UpdateAsync(
                    new UpdateSystemSettingsCommand(
                        "UTC",
                        "preview",
                        "Warning",
                        6,
                        24,
                        20,
                        current.Revision,
                        "https://reports.example.com",
                        48,
                        100),
                    CancellationToken.None);

                Assert.Equal(current.Revision + 1, updated.Revision);
            }

            await using (var readScope = serviceProvider.CreateAsyncScope())
            {
                var settingsService = readScope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
                var updated = await settingsService.GetAsync(CancellationToken.None);

                Assert.Equal("UTC", updated.Timezone);
                Assert.Equal("preview", updated.ReleaseChannel);
                Assert.Equal("Warning", updated.LogLevel);
                Assert.Equal(6, updated.ReportConcurrency);
                Assert.Equal(24, updated.ReportRetentionMonths);
                Assert.Equal(20, updated.BackupRetentionCount);
                Assert.Equal("https://reports.example.com", updated.ReportExternalBaseUrl);
                Assert.Equal(48, updated.ReportDownloadLinkHours);
                Assert.Equal(100, updated.ReportDownloadMaxDownloads);
                Assert.Equal(originalRevision + 1, updated.Revision);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    private const string LegacySchedulingMigration =
        DatabaseMigrationOrchestrator.LegacySchedulingMigration;
    private const string SchedulingMigration = DatabaseMigrationOrchestrator.SchedulingMigration;
    private const string UnixTimestampBackfillBoundary =
        DatabaseMigrationOrchestrator.UnixTimestampBackfillBoundary;
    private const string UnixTimestampValidation = "20260828024507_ValidateUnixTimeBackfill";
    private const string UnixTimestampPhaseTwo =
        DatabaseMigrationOrchestrator.UnixTimestampFinalMigration;

    private static async Task MigrateWithUnixBackfillAsync(ReportDbContext dbContext)
    {
        await DatabaseMigrationOrchestrator.MigrateAsync(dbContext, CancellationToken.None);
    }

    private static async Task<Dictionary<string, string>> GetColumnTypesAsync(
        ReportDbContext dbContext,
        string tableName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(CancellationToken.None);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            columns.Add(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }
}
