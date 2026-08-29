namespace Sub2ApiReport.Updater.Docker;

/// <summary>
/// Docker App 管理接口：只允许通过 Docker Engine API（Docker.DotNet，不执行 shell）管理
/// 带固定 role=app 标签和配置 instance ID 的唯一 App 容器。测试使用注入的替身实现。
/// </summary>
public interface IDockerAppManager
{
    /// <summary>查找当前 App 容器并校验契约。不存在返回 null；发现多个抛出冲突。</summary>
    Task<AppContainerSnapshot?> FindAppContainerAsync(CancellationToken cancellationToken);

    /// <summary>按容器 ID 检查容器是否存在（存在返回快照，不存在返回 null）。</summary>
    Task<AppContainerSnapshot?> FindContainerByIdAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>按升级操作标签查找候选容器（替换与回滚期间使用）。</summary>
    Task<AppContainerSnapshot?> FindContainerByOperationAsync(
        string operationId,
        CancellationToken cancellationToken);

    /// <summary>加载 docker save 归档，并接受签名的 config/target digest 作为后端相关 ID。</summary>
    Task<string> LoadImageArchiveAsync(
        Stream archiveStream,
        string expectedConfigDigest,
        string expectedTargetDigest,
        string expectedLoadedTag,
        string expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>将镜像打上指定标签（如 sub2api-report-app:current）。</summary>
    Task TagImageAsync(string imageId, string repository, string tag, CancellationToken cancellationToken);

    /// <summary>按当前契约快照创建候选 App 容器（附加升级操作标签），返回新容器 ID。</summary>
    Task<string> CreateAppContainerAsync(
        AppContainerSnapshot contract,
        string imageId,
        string operationId,
        CancellationToken cancellationToken);

    Task StartContainerAsync(string containerId, CancellationToken cancellationToken);

    Task StopContainerAsync(string containerId, int waitSeconds, CancellationToken cancellationToken);

    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);

    Task RenameContainerAsync(string containerId, string newName, CancellationToken cancellationToken);
}
