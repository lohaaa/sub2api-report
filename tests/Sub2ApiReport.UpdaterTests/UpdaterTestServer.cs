using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Backup;
using Sub2ApiReport.Updater.Docker;
using Sub2ApiReport.Updater.Install;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests;

internal sealed record UpdaterTestServer(
    TempDirectory TempDirectory,
    WebApplicationFactory<Program> Factory,
    string StateDirectory,
    string? Token) : IDisposable
{
    public HttpClient CreateClient() => Factory.CreateClient();

    public void Dispose()
    {
        Factory.Dispose();
        TempDirectory.Dispose();
    }
}

internal static class UpdaterTestServerFactory
{
    public static string NewToken() => RandomNumberGenerator.GetHexString(64, lowercase: true);

    /// <summary>
    /// 默认用替身替换全部 Docker/App/恢复依赖，确保 WebApplicationFactory 永远不会
    /// 接触真实 Docker daemon 或外部服务；需要时通过 configureServices 覆盖。
    /// </summary>
    public static UpdaterTestServer Create(
        string? publicKeyPem = null,
        IGitHubReleaseClient? releaseClient = null,
        IDownloader? downloader = null,
        bool withToken = true,
        Action<IServiceCollection>? configureServices = null,
        bool installationEnabled = false)
    {
        var temp = new TempDirectory();
        var stateDirectory = Path.Combine(temp.FullPath, "state");
        Directory.CreateDirectory(stateDirectory);
        var databasePath = Path.Combine(temp.FullPath, "db", "sub2api-report.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        // 零字节文件是合法的空 SQLite 数据库；preflight 只检查文件可访问。
        File.WriteAllText(databasePath, string.Empty);

        var token = NewToken();
        var tokenPath = Path.Combine(temp.FullPath, "updater-token");
        if (withToken)
        {
            File.WriteAllText(tokenPath, token);
        }

        var publicKeyPath = Path.Combine(temp.FullPath, "release-public-key.pem");
        if (publicKeyPem is not null)
        {
            File.WriteAllText(publicKeyPath, publicKeyPem);
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Updater:TokenFile", withToken ? tokenPath : Path.Combine(temp.FullPath, "missing"));
            builder.UseSetting("Updater:PublicKeyPath", publicKeyPath);
            builder.UseSetting("Updater:StatePath", stateDirectory);
            builder.UseSetting("Updater:DatabasePath", databasePath);
            builder.UseSetting("Updater:InstallationEnabled", installationEnabled ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                if (releaseClient is not null)
                {
                    services.RemoveAll<IGitHubReleaseClient>();
                    services.AddSingleton(releaseClient);
                }

                if (downloader is not null)
                {
                    services.RemoveAll<IDownloader>();
                    services.AddSingleton(downloader);
                }

                // 默认全部替换，隔离真实 Docker / 网络。
                services.RemoveAll<IDockerAppManager>();
                services.AddSingleton<IDockerAppManager, FakeDockerAppManager>();
                services.RemoveAll<IAppMaintenanceClient>();
                services.AddSingleton<IAppMaintenanceClient, FakeMaintenanceClient>();
                services.RemoveAll<IHealthVerifier>();
                services.AddSingleton<IHealthVerifier, FakeHealthVerifier>();
                services.RemoveAll<ISqliteBackupService>();
                services.AddSingleton<ISqliteBackupService, FakeBackupService>();
                services.RemoveAll<IInstallRecovery>();
                services.AddSingleton<IInstallRecovery, FakeInstallRecovery>();

                configureServices?.Invoke(services);
            });
        });

        return new UpdaterTestServer(temp, factory, stateDirectory, withToken ? token : null);
    }
}
