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

    private readonly IReadOnlyDictionary<string, string?> _settings;
    public ApiWebApplicationFactory()
        : this(null, true, Environments.Development)
    {
    }

    internal ApiWebApplicationFactory(
        string? databasePath,
        bool deleteDatabaseOnDispose,
        string environmentName = "Development",
        Action<IServiceCollection>? configureTestServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
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
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.UseSetting("ConnectionStrings:Database", $"Data Source={_databasePath}");
        builder.UseSetting("DataProtection:KeysPath", _dataProtectionKeysPath);
        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = $"Data Source={_databasePath}",
                ["DataProtection:KeysPath"] = _dataProtectionKeysPath,
            };
            foreach (var setting in _settings)
            {
                values[setting.Key] = setting.Value;
            }

            configuration.AddInMemoryCollection(values);
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
            DatabaseMigrationOrchestrator.MigrateAsync(dbContext, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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
