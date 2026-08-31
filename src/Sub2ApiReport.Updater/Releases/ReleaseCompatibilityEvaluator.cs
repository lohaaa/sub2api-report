using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Updater.Releases;

public sealed record ReleaseCompatibilityResult(bool OnlineInstallAllowed, string Message);

public static class ReleaseCompatibilityEvaluator
{
    public static ReleaseCompatibilityResult Evaluate(
        ReleaseManifest manifest,
        string currentAppVersion,
        string currentUpdaterVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAppVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUpdaterVersion);

        if (manifest.ManualUpgradeRequired || !manifest.OnlineInstallSupported)
        {
            return new(false, manifest.UpgradeMessage);
        }

        if (!manifest.OnlineUpgradeFrom.Contains(currentAppVersion, StringComparer.Ordinal))
        {
            return new(
                false,
                $"未验证从 App {currentAppVersion} 在线升级到 {manifest.Version}，请使用完整 Release bundle。");
        }

        if (!SemanticVersion.TryParse(manifest.MinimumUpdaterVersion, out var minimumUpdaterVersion)
            || !SemanticVersion.TryParse(currentUpdaterVersion, out var updaterVersion)
            || updaterVersion!.CompareTo(minimumUpdaterVersion) < 0)
        {
            return new(
                false,
                $"当前 Updater {currentUpdaterVersion} 不满足目标版本要求，请使用完整 Release bundle。");
        }

        return new(true, manifest.UpgradeMessage);
    }
}
