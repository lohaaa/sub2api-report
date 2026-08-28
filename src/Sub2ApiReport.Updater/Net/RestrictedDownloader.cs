using System.Net;

using System.Security.Cryptography;
using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.Updater.Net;

public sealed record DownloadResult(
    string FilePath,
    long Size,
    string Sha256);

public interface IDownloader
{
    /// <summary>
    /// 流式下载到目标路径（先写 .partial，成功后原子改名），限制总大小并返回 SHA-256。
    /// </summary>
    Task<DownloadResult> DownloadAsync(
        string url,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken);
}

/// <summary>
/// 受限下载器：仅允许固定 HTTPS host，禁止重定向到允许列表之外，流式写入并限制总大小。
/// </summary>
public sealed class RestrictedDownloader(HttpClient httpClient) : IDownloader
{
    private const int MaximumRedirects = 4;
    private const int BufferSize = 81920;
    private static readonly HashSet<HttpStatusCode> RedirectStatuses =
        new HashSet<HttpStatusCode>
        {
            HttpStatusCode.MovedPermanently,
            HttpStatusCode.Found,
            HttpStatusCode.SeeOther,
            HttpStatusCode.TemporaryRedirect,
            HttpStatusCode.PermanentRedirect,
        };

    public async Task<DownloadResult> DownloadAsync(
        string url,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var current = ValidateUrl(url);
        var partialPath = $"{destinationPath}.partial";
        HttpResponseMessage response;
        for (var redirects = 0; ; redirects++)
        {
            if (redirects > MaximumRedirects)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "下载重定向次数超出限制。");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            using (response)
            {
                if (RedirectStatuses.Contains(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw new UpdateOperationException(
                            StatusCodes.Status502BadGateway,
                            "下载重定向缺少目标地址。");
                    }

                    var next = new Uri(current, location);
                    current = ValidateUrl(next.ToString());
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new UpdateOperationException(
                        StatusCodes.Status502BadGateway,
                        "Release 资产下载失败。");
                }

                return await WriteVerifiedFileAsync(response, partialPath, destinationPath, maxBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task<DownloadResult> WriteVerifiedFileAsync(
        HttpResponseMessage response,
        string partialPath,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(partialPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 上一次中止可能残留 .partial；安全删除后重建，避免 CreateNew 失败或读到旧内容。
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            await using var contentStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = await contentStream
                       .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                {
                    throw new UpdateOperationException(
                        StatusCodes.Status502BadGateway,
                        "下载内容超出允许的大小上限。");
                }

                hash.AppendData(buffer, 0, bytesRead);
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            await output.DisposeAsync().ConfigureAwait(false);
            File.Move(partialPath, destinationPath, overwrite: true);
            return new DownloadResult(destinationPath, totalBytes, sha256);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.IsNullOrEmpty(uri.Host)
            || !GitHubReleaseLocations.IsAllowedDownloadHost(uri.Host))
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "下载地址不在固定 Release 允许列表内。");
        }

        return uri;
    }
}
