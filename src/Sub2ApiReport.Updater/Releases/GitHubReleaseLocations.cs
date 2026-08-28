namespace Sub2ApiReport.Updater.Releases;

/// <summary>
/// 固定的 GitHub 发布位置与 URL 校验规则。任何配置、请求或 Release 内容都不能改写这些值。
/// </summary>
public static class GitHubReleaseLocations
{
    public const string Owner = "lohaaa";
    public const string Repository = "sub2api-report";
    public const string ManifestAssetName = "release-manifest.json";
    public const string ManifestSignatureAssetName = "release-manifest.sig";
    public const string ArchitectureSuffix = "linux-amd64";

    public static string LatestReleaseApiUrl { get; } =
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    private static readonly HashSet<string> AllowedDownloadHosts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        };

    public static bool IsAllowedDownloadHost(string? host) =>
        host is not null && AllowedDownloadHosts.Contains(host);

    public static string GetAppArchiveFileName(string version) =>
        $"sub2api-report-app-v{version}-{ArchitectureSuffix}.tar.gz";

    public static string GetUpdaterArchiveFileName(string version) =>
        $"sub2api-report-updater-v{version}-{ArchitectureSuffix}.tar.gz";

    public static string GetReleaseNotesFileName(string version) =>
        $"release-notes-v{version}.md";

    public static string GetReleasePageUrl(string version) =>
        $"https://github.com/{Owner}/{Repository}/releases/tag/v{version}";

    /// <summary>
    /// 只接受固定仓库固定 Tag 下的指定 Release 资产 HTTPS URL，禁止端口、用户信息、路径遍历等变体。
    /// </summary>
    public static bool IsAllowedReleaseAssetUrl(string? url, string expectedTag, string expectedFileName)
    {
        if (string.IsNullOrEmpty(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !uri.IsDefaultPort
            || uri.Query.Length > 0
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 6
            && segments.All(segment => !segment.Contains("..", StringComparison.Ordinal))
            && string.Equals(segments[0], Owner, StringComparison.Ordinal)
            && string.Equals(segments[1], Repository, StringComparison.Ordinal)
            && string.Equals(segments[2], "releases", StringComparison.Ordinal)
            && string.Equals(segments[3], "download", StringComparison.Ordinal)
            && string.Equals(segments[4], expectedTag, StringComparison.Ordinal)
            && string.Equals(segments[5], expectedFileName, StringComparison.Ordinal);
    }
}
