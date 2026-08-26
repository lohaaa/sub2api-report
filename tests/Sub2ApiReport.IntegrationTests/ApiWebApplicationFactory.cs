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
    private readonly string _databasePath;
    private readonly string _dataProtectionKeysPath;
    private readonly bool _deleteDatabaseOnDispose;
    private readonly string _environmentName;
    private readonly Action<IServiceCollection>? _configureTestServices;

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
        _databasePath = databasePath ?? Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-api-{Guid.NewGuid():N}.db");
        _dataProtectionKeysPath = Path.Combine(
            Path.GetTempPath(),
            $"sub2api-report-keys-{Guid.NewGuid():N}");
        _deleteDatabaseOnDispose = deleteDatabaseOnDispose;
        _environmentName = environmentName;
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = $"Data Source={_databasePath}",
                ["DataProtection:KeysPath"] = _dataProtectionKeysPath,
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ReportDbContext>>();
            services.RemoveAll<ReportDbContext>();
            services.AddDbContext<ReportDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
            _configureTestServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
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

        if (_deleteDatabaseOnDispose)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
            File.Delete($"{_databasePath}-shm");
            File.Delete($"{_databasePath}-wal");
        }

        if (Directory.Exists(_dataProtectionKeysPath))
        {
            Directory.Delete(_dataProtectionKeysPath, recursive: true);
        }
    }
}
