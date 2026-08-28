using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests.Releases;

public sealed class ReleaseUrlPolicyTests
{
    private const string Tag = "v1.2.0";

    [Fact]
    public void AcceptsFixedReleaseAssetUrl()
    {
        var url = $"https://github.com/lohaaa/sub2api-report/releases/download/{Tag}/release-manifest.json";
        Assert.True(GitHubReleaseLocations.IsAllowedReleaseAssetUrl(
            url, Tag, "release-manifest.json"));
    }

    [Fact]
    public void AcceptsAppArchiveUrl()
    {
        var url = TestReleases.AppArchiveUrl("1.2.0");
        Assert.True(GitHubReleaseLocations.IsAllowedReleaseAssetUrl(
            url, Tag, GitHubReleaseLocations.GetAppArchiveFileName("1.2.0")));
    }

    [Theory]
    [InlineData("http://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json?x=1")]
    [InlineData("https://github.com:8443/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://user:pass@github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://github.com.evil.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://evil.example/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://github.com/evil/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://github.com/lohaaa/other-repo/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.1/release-manifest.json")]
    [InlineData("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.sig")]
    [InlineData("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/..%2Frelease-manifest.json")]
    [InlineData("https://github.com/lohaaa/sub2api-report/releases/download/v1.2.0/extra/release-manifest.json")]
    [InlineData("/lohaaa/sub2api-report/releases/download/v1.2.0/release-manifest.json")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsUrlsOutsideFixedReleasePath(string? url)
    {
        Assert.False(GitHubReleaseLocations.IsAllowedReleaseAssetUrl(
            url, Tag, "release-manifest.json"));
    }

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("objects.githubusercontent.com", true)]
    [InlineData("release-assets.githubusercontent.com", true)]
    [InlineData("GitHub.com", true)]
    [InlineData("evil.example", false)]
    [InlineData("github.com.evil.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AppliesDownloadHostAllowlist(string? host, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseLocations.IsAllowedDownloadHost(host));
    }
}
