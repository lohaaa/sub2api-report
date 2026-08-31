using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests.Releases;

public sealed class ReleaseCompatibilityEvaluatorTests
{
    [Fact]
    public void ManualReleaseRequiresBundle()
    {
        var manifest = new ReleaseManifestBuilder()
            .WithUpgradeMessage("必须使用完整 bundle。")
            .Build();

        var result = ReleaseCompatibilityEvaluator.Evaluate(manifest, "1.1.1", "1.1.1");

        Assert.False(result.OnlineInstallAllowed);
        Assert.Equal("必须使用完整 bundle。", result.Message);
    }

    [Fact]
    public void RejectsUnverifiedSourceVersion()
    {
        var manifest = OnlineManifest("1.1.1");

        var result = ReleaseCompatibilityEvaluator.Evaluate(manifest, "1.1.0", "1.1.1");

        Assert.False(result.OnlineInstallAllowed);
        Assert.Contains("未验证从 App 1.1.0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUpdaterBelowMinimum()
    {
        var manifest = OnlineManifest("1.1.1") with { MinimumUpdaterVersion = "1.1.1" };

        var result = ReleaseCompatibilityEvaluator.Evaluate(manifest, "1.1.1", "1.1.0");

        Assert.False(result.OnlineInstallAllowed);
        Assert.Contains("Updater 1.1.0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsExplicitlyVerifiedSourceAndUpdater()
    {
        var manifest = OnlineManifest("1.1.1") with { MinimumUpdaterVersion = "1.1.0" };

        var result = ReleaseCompatibilityEvaluator.Evaluate(manifest, "1.1.1", "1.1.1");

        Assert.True(result.OnlineInstallAllowed);
        Assert.Equal("支持在线升级。", result.Message);
    }

    private static Sub2ApiReport.UpdateContracts.ReleaseManifest OnlineManifest(string sourceVersion) =>
        new ReleaseManifestBuilder()
            .WithManualUpgradeRequired(false)
            .WithOnlineInstallSupported(true)
            .WithOnlineUpgradeFrom(sourceVersion)
            .WithUpgradeMessage("支持在线升级。")
            .Build();
}
