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
}
