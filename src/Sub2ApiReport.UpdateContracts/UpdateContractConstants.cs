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

    /// <summary>App 容器角色标签键（deployment contract v1）。</summary>
    public const string AppRoleLabelKey = "sub2api-report.role";

    /// <summary>App 容器角色标签值。</summary>
    public const string AppRoleLabelValue = "app";

    /// <summary>部署实例 ID 标签键，与 Compose .env 中生成的 instance ID 对应。</summary>
    public const string InstanceLabelKey = "sub2api-report.instance";

    /// <summary>升级操作标签键，用于在替换与回滚期间定位候选容器。</summary>
    public const string UpgradeOperationLabelKey = "sub2api-report.upgrade-operation";

    /// <summary>镜像版本 label 键（OCI 标准）。</summary>
    public const string ImageVersionLabelKey = "org.opencontainers.image.version";

    /// <summary>当前 App 本地镜像标签。</summary>
    public const string AppCurrentImageRepository = "sub2api-report-app";
    public const string AppCurrentImageTagName = "current";

    /// <summary>App 内部端口与数据挂载点（deployment contract v1）。</summary>
    public const string AppInternalPort = "8080";
    public const string AppDataMountTarget = "/managed-data";
}
