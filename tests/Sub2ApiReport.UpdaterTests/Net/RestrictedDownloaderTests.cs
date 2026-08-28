using System.Net;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Net;

namespace Sub2ApiReport.UpdaterTests.Net;

public sealed class RestrictedDownloaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly List<StubHttpHandler> _handlers = [];

    [Fact]
    public async Task WritesDestinationAndComputesSha256()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var destination = Path.Combine(_temp.FullPath, "downloads", "archive.bin");
        var (downloader, handler) = CreateDownloader(_ => Ok(content));

        var result = await downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            destination,
            maxBytes: 1024,
            CancellationToken.None);

        Assert.Equal(content.Length, result.Size);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant(),
            result.Sha256);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists($"{destination}.partial"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RecoversFromStalePartialFile()
    {
        var content = new byte[] { 9, 8, 7 };
        var destination = Path.Combine(_temp.FullPath, "archive.bin");
        Directory.CreateDirectory(_temp.FullPath);
        await File.WriteAllTextAsync($"{destination}.partial", "stale leftovers from aborted run");
        var (downloader, _) = CreateDownloader(_ => Ok(content));

        var result = await downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            destination,
            maxBytes: 1024,
            CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.Equal(content.Length, result.Size);
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task CreatesMissingParentDirectory()
    {
        var destination = Path.Combine(_temp.FullPath, "a", "b", "archive.bin");
        var (downloader, _) = CreateDownloader(_ => Ok([1]));

        await downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            destination,
            maxBytes: 1024,
            CancellationToken.None);

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task RejectsContentOverSizeLimit()
    {
        var content = new byte[] { 1, 2, 3 };
        var destination = Path.Combine(_temp.FullPath, "archive.bin");
        var (downloader, _) = CreateDownloader(_ => Ok(content));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            destination,
            maxBytes: 2,
            CancellationToken.None));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task FollowsAllowlistedRedirect()
    {
        var content = new byte[] { 4, 5, 6 };
        var (downloader, handler) = CreateDownloader(request => request.RequestUri!.Host switch
        {
            "github.com" => Redirect("https://objects.githubusercontent.com/asset/1"),
            _ => Ok(content),
        });

        var result = await downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(result.FilePath));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("objects.githubusercontent.com", handler.Requests[1].Host);
    }

    [Fact]
    public async Task RejectsRedirectToDisallowedHost()
    {
        var (downloader, handler) = CreateDownloader(_ =>
            Redirect("https://evil.example/lohaaa/sub2api-report/releases/download/v1.2.0/x"));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RejectsRedirectToNonHttps()
    {
        var (downloader, _) = CreateDownloader(_ =>
            Redirect("http://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json"));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNonHttpsInitialUrl()
    {
        var (downloader, _) = CreateDownloader(_ => Ok([1]));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "http://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsDisallowedInitialHost()
    {
        var (downloader, _) = CreateDownloader(_ => Ok([1]));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://evil.example/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTooManyRedirects()
    {
        var count = 0;
        var (downloader, _) = CreateDownloader(_ =>
        {
            count++;
            return count % 2 == 1
                ? Redirect("https://objects.githubusercontent.com/asset/1")
                : Redirect("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json");
        });

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsHttpErrorStatus()
    {
        var (downloader, _) = CreateDownloader(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<UpdateOperationException>(() => downloader.DownloadAsync(
            "https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json",
            Path.Combine(_temp.FullPath, "archive.bin"),
            maxBytes: 1024,
            CancellationToken.None));
    }

    public void Dispose() => _temp.Dispose();

    private (RestrictedDownloader Downloader, StubHttpHandler Handler) CreateDownloader(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpHandler(responder);
        _handlers.Add(handler);
        return (new RestrictedDownloader(new HttpClient(handler)), handler);
    }

    private static HttpResponseMessage Ok(byte[] content) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };
}
