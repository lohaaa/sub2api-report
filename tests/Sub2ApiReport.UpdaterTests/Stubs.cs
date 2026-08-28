using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Net;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests;

internal sealed class StubGitHubReleaseClient(GitHubReleaseInfo release) : IGitHubReleaseClient
{
    public Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult(release);
}

internal sealed class StubDownloader(IReadOnlyDictionary<string, byte[]> contents) : IDownloader
{
    public Task<DownloadResult> DownloadAsync(
        string url,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (!contents.TryGetValue(url, out var bytes))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "没有可用的下载内容。");
        }

        if (bytes.Length > maxBytes)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "下载内容超出允许的大小上限。");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(destinationPath, bytes);
        return Task.FromResult(new DownloadResult(
            destinationPath,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
    }
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(responder(request));
    }
}
