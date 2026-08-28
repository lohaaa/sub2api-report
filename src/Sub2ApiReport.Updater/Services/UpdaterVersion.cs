using System.Reflection;

using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.Updater.Services;

public static class UpdaterVersion
{
    public static string GetCurrent()
    {
        var assembly = typeof(UpdaterVersion).Assembly;
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    public static bool TryGetCurrent(out SemanticVersion version) =>
        SemanticVersion.TryParse(GetCurrent(), out version!);
}
