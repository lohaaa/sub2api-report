using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath;
    private readonly bool deleteDatabaseOnDispose;
    private readonly string environmentName;
    private readonly Action<IServiceCollection>? configureTestServices;

    public ApiWebApplicationFactory()
        : this(null, true, Environments.Development)
    {
    }

    internal ApiWebApplicationFactory(
        string? databasePath,
        bool deleteDatabaseOnDispose,
        string environmentName = "Development",
        Action<IServiceCollection>? configureTestServices = null)
    {
        this.databasePath = databasePath ?? Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-api-{Guid.NewGuid():N}.db");
        this.deleteDatabaseOnDispose = deleteDatabaseOnDispose;
        this.environmentName = environmentName;
        this.configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = $"Data Source={databasePath}",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ReportDbContext>>();
            services.RemoveAll<ReportDbContext>();
            services.AddDbContext<ReportDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            configureTestServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        using (var dbContext = new ReportDbContext(options))
        {
            dbContext.Database.Migrate();
        }

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        if (deleteDatabaseOnDispose)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }
}
