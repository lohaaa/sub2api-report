using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sub2ApiReport.Updater;
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

    public static UpdaterTestServer Create(
        string? publicKeyPem = null,
        IGitHubReleaseClient? releaseClient = null,
        IDownloader? downloader = null,
        bool withToken = true)
    {
        var temp = new TempDirectory();
        var stateDirectory = Path.Combine(temp.FullPath, "state");
        Directory.CreateDirectory(stateDirectory);

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
            });
        });

        return new UpdaterTestServer(temp, factory, stateDirectory, withToken ? token : null);
    }
}
