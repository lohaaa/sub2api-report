namespace Sub2ApiReport.Updater;

public sealed class UpdateOptions
{
    public const string SectionName = "Updater";

    /// <summary>
    /// 共享令牌文件路径（对应 Compose 的 Updater__TokenFile）。文件内容必须是 64 位十六进制字符，
    /// 以只读方式加载；未配置或内容无效时内部 API 一律拒绝（fail closed）。禁止记录令牌内容。
    /// </summary>
    public string TokenFile { get; set; } = "/run/secrets/updater-token";

    /// <summary>发布签名公钥文件路径（PEM，只读）。</summary>
    public string PublicKeyPath { get; set; } = "keys/release-public-key.pem";

    /// <summary>升级状态目录，存放持久化状态、缓存和下载临时文件。</summary>
    public string StatePath { get; set; } = "update-state";

    /// <summary>App 镜像归档下载大小上限（字节）。</summary>
    public long MaxDownloadBytes { get; set; } = 1_073_741_824;

    /// <summary>Release manifest 等元数据下载大小上限（字节）。</summary>
    public long MaxManifestBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// 是否允许在线安装。必须保持 config-gated：Compose 中默认 false，
    /// 只有完成威胁建模、审计并挂载 Docker Socket 后才在部署配置中显式开启。
    /// </summary>
    public bool InstallationEnabled { get; set; }

    /// <summary>部署实例 ID（与 App 容器 instance 标签一致）。</summary>
    public string AppInstanceId { get; set; } = "default";

    /// <summary>Docker Engine 地址（仅 unix socket，Updater 是唯一挂载 Socket 的组件）。</summary>
    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>App 内部服务地址（Compose 私有网络内的服务名）。</summary>
    public string AppInternalBaseUrl { get; set; } = "http://app:8080";

    /// <summary>App SQLite 数据库文件路径（与 App 共享的 named 数据卷挂载点）。</summary>
    public string DatabasePath { get; set; } = "/managed-data/db/sub2api-report.db";

    /// <summary>升级验证窗口（秒）。</summary>
    public int VerifyTimeoutSeconds { get; set; } = 120;

    /// <summary>升级验证要求的连续成功次数（live/ready/握手/版本）。</summary>
    public int VerifyConsecutiveSuccesses { get; set; } = 3;

    /// <summary>停止旧容器的等待秒数。</summary>
    public int ContainerStopWaitSeconds { get; set; } = 30;
}
