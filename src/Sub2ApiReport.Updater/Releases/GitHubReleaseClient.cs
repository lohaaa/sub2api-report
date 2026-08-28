using System.Globalization;
using System.Text.Json;

namespace Sub2ApiReport.Updater.Releases;

public sealed record GitHubReleaseAsset(
    string Name,
    string DownloadUrl,
    long Size);

public sealed record GitHubReleaseInfo(
    string TagName,
    DateTimeOffset PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets);

public interface IGitHubReleaseClient
{
    Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 固定仓库 lohaaa/sub2api-report 的最新 Release 发现。只使用固定的 HTTPS API 地址。
/// </summary>
public sealed class GitHubReleaseClient(HttpClient httpClient) : IGitHubReleaseClient
{
    private const long MaximumMetadataBytes = 2 * 1024 * 1024;
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = 32,
    };

    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(GitHubReleaseLocations.LatestReleaseApiUrl));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "GitHub Release 服务暂不可用。",
                exception);
        }

        using (response)
        {
            if ((int)response.StatusCode is 403 or 429)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "GitHub API 访问被限流，请稍后重试。");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "GitHub API 请求失败。");
            }

            try
            {
                await response.Content.LoadIntoBufferAsync(MaximumMetadataBytes, cancellationToken);
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var document = await JsonDocument
                    .ParseAsync(stream, DocumentOptions, cancellationToken)
                    .ConfigureAwait(false);
                return Parse(document.RootElement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException or InvalidOperationException)
            {
                throw new UpdateOperationException(
                    StatusCodes.Status502BadGateway,
                    "GitHub API 返回了无效数据。",
                    exception);
            }
        }
    }

    private static GitHubReleaseInfo Parse(JsonElement root)
    {
        var tagName = root.GetProperty("tag_name").GetString() ?? throw InvalidPayload();
        if (!tagName.StartsWith('v')
            || !SemanticVersion.TryParse(tagName[1..], out _))
        {
            throw InvalidPayload();
        }

        if (root.TryGetProperty("draft", out var draftElement) && draftElement.GetBoolean())
        {
            throw InvalidPayload();
        }

        if (root.TryGetProperty("prerelease", out var prereleaseElement) && prereleaseElement.GetBoolean())
        {
            throw InvalidPayload();
        }

        if (!root.TryGetProperty("published_at", out var publishedElement)
            || publishedElement.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                publishedElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var publishedAt))
        {
            throw InvalidPayload();
        }

        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidPayload();
        }

        var assets = new List<GitHubReleaseAsset>();
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = assetElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var downloadUrl = assetElement.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            var size = assetElement.TryGetProperty("size", out var sizeElement)
                && sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : -1;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl) || size < 0)
            {
                throw InvalidPayload();
            }

            assets.Add(new GitHubReleaseAsset(name, downloadUrl, size));
        }

        return new GitHubReleaseInfo(tagName, publishedAt, assets);
    }

    private static UpdateOperationException InvalidPayload() =>
        new(StatusCodes.Status502BadGateway, "GitHub API 返回了无效数据。");
}
