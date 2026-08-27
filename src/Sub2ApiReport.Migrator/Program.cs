using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sub2ApiReport.Infrastructure;
using Sub2ApiReport.Infrastructure.Persistence;
using Sub2ApiReport.Migrator;

var builder = Host.CreateApplicationBuilder(args);
var configuredConnectionString = Environment.GetEnvironmentVariable("SUB2API_REPORT_DATABASE")
    ?? builder.Configuration.GetConnectionString("Database")
    ?? DatabaseDefaults.ConnectionString;
var connectionString = DatabaseDefaults.ResolveConnectionString(
    configuredConnectionString,
    builder.Environment.ContentRootPath);

EnsureDatabaseDirectory(connectionString);
_ = builder.Services.AddAuthentication();
_ = builder.Services.AddDataProtection();
_ = builder.Services.AddInfrastructure(connectionString);

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Sub2ApiReport.Migrator");
var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();

MigrationLog.Applying(logger);
await dbContext.Database.MigrateAsync();
await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
await dbContext.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
MigrationLog.Completed(logger);

static void EnsureDatabaseDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
    {
        return;
    }

    var fullPath = Path.GetFullPath(dataSource);
    var directory = Path.GetDirectoryName(fullPath);
    if (directory is not null)
    {
        Directory.CreateDirectory(directory);
    }
}
