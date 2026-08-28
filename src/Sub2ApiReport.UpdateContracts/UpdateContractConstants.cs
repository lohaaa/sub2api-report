namespace Sub2ApiReport.UpdateContracts;

public static class UpdateContractConstants
{
    public const int ManifestSchemaVersion = 1;
    public const int DeploymentContractVersion = 1;
    public const string SignatureAlgorithm = "RSASSA-PKCS1-v1_5-SHA256";
    public const string StableChannel = "stable";
    public const string Architecture = "linux/amd64";
    public const string AppLoadedTagPrefix = "sub2api-report-app:";
    public const string UpdaterLoadedTagPrefix = "sub2api-report-updater:";
}
