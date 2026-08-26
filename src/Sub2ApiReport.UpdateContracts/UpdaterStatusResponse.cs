namespace Sub2ApiReport.UpdateContracts;

public sealed record UpdaterStatusResponse(
    string Version,
    bool InstallationEnabled,
    string State);
