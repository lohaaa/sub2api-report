using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Infrastructure;
using Sub2ApiReport.Infrastructure.Identity;
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
        await dbContext.Database.MigrateAsync(CancellationToken.None);
        var setting = await dbContext.SystemSettings.SingleAsync(CancellationToken.None);

        Assert.Equal(1, setting.Id);
        Assert.Equal("Asia/Shanghai", setting.Timezone);
        Assert.Equal("stable", setting.ReleaseChannel);
        Assert.Equal("Information", setting.LogLevel);
        Assert.Equal(12, setting.ReportRetentionMonths);
        Assert.Equal(10, setting.BackupRetentionCount);
        Assert.Equal(1, setting.Revision);
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
        var setting = await dbContext.SystemSettings.SingleAsync(CancellationToken.None);
        setting.MarkInitialized(initializedAt);
        dbContext.Users.Add(Administrator.Create("synthetic-admin", initializedAt));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await migrator.MigrateAsync(cancellationToken: CancellationToken.None);

        Assert.Single(await dbContext.Users.AsNoTracking().ToListAsync(CancellationToken.None));
        Assert.Equal(initializedAt, (await dbContext.SystemSettings.AsNoTracking().SingleAsync(CancellationToken.None)).InitializedAt);
        Assert.Empty(await dbContext.Sub2ApiConnections.AsNoTracking().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.People.AsNoTracking().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.ExternalApiKeys.AsNoTracking().ToListAsync(CancellationToken.None));
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
                await dbContext.Database.MigrateAsync(CancellationToken.None);
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
                        24,
                        20,
                        current.Revision),
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
                Assert.Equal(24, updated.ReportRetentionMonths);
                Assert.Equal(20, updated.BackupRetentionCount);
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
}
