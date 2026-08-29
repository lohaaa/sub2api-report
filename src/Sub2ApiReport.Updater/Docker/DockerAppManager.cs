using Docker.DotNet;
using Docker.DotNet.Models;
using Sub2ApiReport.UpdateContracts;

namespace Sub2ApiReport.Updater.Docker;

/// <summary>
/// 基于 Docker.DotNet 的 App 容器管理生产实现。只使用 Docker Engine HTTP API，
/// 禁止执行任何 shell 命令；只匹配 role=app + 配置 instance ID 标签的唯一容器。
/// </summary>
public sealed class DockerAppManager(DockerClient client, UpdateOptions options) : IDockerAppManager
{
    private readonly DockerClient _client = client;

    public async Task<AppContainerSnapshot?> FindAppContainerAsync(CancellationToken cancellationToken)
    {
        var containers = await ListAppContainersAsync(cancellationToken);
        if (containers.Count == 0)
        {
            return null;
        }

        if (containers.Count > 1)
        {
            throw new UpdateOperationException(
                StatusCodes.Status409Conflict,
                "发现多个匹配当前实例的 App 容器，拒绝安装。");
        }

        return await InspectContainerAsync(containers[0].ID, cancellationToken);
    }

    public async Task<AppContainerSnapshot?> FindContainerByIdAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await InspectContainerAsync(containerId, cancellationToken);
        }
        catch (UpdateOperationException exception)
            when (exception.InnerException is DockerApiException apiException
                && apiException.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<AppContainerSnapshot?> FindContainerByOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{UpdateContractConstants.UpgradeOperationLabelKey}={operationId}"] = true,
                    },
                },
            },
            cancellationToken);

        if (containers.Count == 0)
        {
            return null;
        }

        if (containers.Count > 1)
        {
            throw new UpdateOperationException(
                StatusCodes.Status409Conflict,
                "升级操作标签匹配到多个容器，拒绝继续。");
        }

        return await InspectContainerAsync(containers[0].ID, cancellationToken);
    }

    public async Task<string> LoadImageArchiveAsync(
        Stream archiveStream,
        string expectedConfigDigest,
        string expectedTargetDigest,
        string expectedLoadedTag,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        var loadErrors = new List<string>();
        var progress = new Progress<JSONMessage>(message =>
        {
            if (message.Error is not null || !string.IsNullOrEmpty(message.ErrorMessage))
            {
                lock (loadErrors)
                {
                    loadErrors.Add(message.ErrorMessage ?? message.Error?.Message ?? "镜像加载失败。");
                }
            }
        });

        try
        {
            await _client.Images.LoadImageAsync(
                new ImageLoadParameters { Quiet = true },
                archiveStream,
                progress,
                cancellationToken);
        }
        catch (DockerApiException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "镜像归档加载失败。",
                exception);
        }

        if (loadErrors.Count > 0)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "镜像归档加载失败。");
        }

        ImageInspectResponse image;
        try
        {
            image = await _client.Images.InspectImageAsync(expectedLoadedTag, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "归档中未包含 manifest 声明的镜像。");
        }

        ValidateLoadedImage(
            image,
            expectedConfigDigest,
            expectedTargetDigest,
            expectedLoadedTag,
            expectedVersion);
        return image.ID;
    }

    public async Task TagImageAsync(
        string imageId,
        string repository,
        string tag,
        CancellationToken cancellationToken)
    {
        await _client.Images.TagImageAsync(
            imageId,
            new ImageTagParameters { RepositoryName = repository, Tag = tag },
            cancellationToken);
    }

    public async Task<string> CreateAppContainerAsync(
        AppContainerSnapshot contract,
        string imageId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var parameters = AppContractMapper.ToCreateParameters(contract, imageId, operationId);
        CreateContainerResponse response;
        try
        {
            response = await _client.Containers.CreateContainerAsync(parameters, cancellationToken);
        }
        catch (DockerApiException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "候选 App 容器创建失败。",
                exception);
        }

        return response.ID;
    }

    public async Task StartContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
        }
        catch (DockerApiException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "容器启动失败。",
                exception);
        }
    }

    public async Task StopContainerAsync(string containerId, int waitSeconds, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)Math.Max(0, waitSeconds) },
                cancellationToken);
        }
        catch (DockerApiException)
        {
            // 已停止或已退出的容器返回 304/404，视为停止成功。
        }
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
                cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 容器已不存在，删除成功。
        }
    }

    public async Task RenameContainerAsync(string containerId, string newName, CancellationToken cancellationToken)
    {
        await _client.Containers.RenameContainerAsync(
            containerId,
            new ContainerRenameParameters { NewName = newName },
            cancellationToken);
    }

    private async Task<IReadOnlyList<ContainerListResponse>> ListAppContainersAsync(
        CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{UpdateContractConstants.AppRoleLabelKey}={UpdateContractConstants.AppRoleLabelValue}"] = true,
                        [$"{UpdateContractConstants.InstanceLabelKey}={options.AppInstanceId}"] = true,
                    },
                },
            },
            cancellationToken);
        return containers.ToList();
    }

    private async Task<AppContainerSnapshot> InspectContainerAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        ContainerInspectResponse inspect;
        try
        {
            inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
        }
        catch (DockerApiException exception)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "App 容器检查失败。",
                exception);
        }

        return AppContractMapper.MapSnapshot(inspect);
    }

    internal static void ValidateLoadedImage(
        ImageInspectResponse image,
        string expectedConfigDigest,
        string expectedTargetDigest,
        string expectedLoadedTag,
        string expectedVersion)
    {
        var errors = new List<string>();
        if (!string.Equals(image.ID, expectedConfigDigest, StringComparison.Ordinal)
            && !string.Equals(image.ID, expectedTargetDigest, StringComparison.Ordinal))
        {
            errors.Add("加载后的镜像 ID 与签名的 config/target digest 均不一致。");
        }

        if (!string.Equals(image.Os, "linux", StringComparison.Ordinal))
        {
            errors.Add("加载后的镜像 OS 不是 linux。");
        }

        if (!string.Equals(image.Architecture, "amd64", StringComparison.Ordinal))
        {
            errors.Add("加载后的镜像架构不是 amd64。");
        }

        var labels = image.Config?.Labels;
        if (labels is null
            || !labels.TryGetValue(
                UpdateContractConstants.ImageVersionLabelKey,
                out var imageVersion)
            || !string.Equals(imageVersion, expectedVersion, StringComparison.Ordinal))
        {
            errors.Add("加载后的镜像缺少正确的版本 label。");
        }

        if (labels is null
            || !labels.TryGetValue(UpdateContractConstants.AppRoleLabelKey, out var imageRole)
            || !string.Equals(imageRole, UpdateContractConstants.AppRoleLabelValue, StringComparison.Ordinal))
        {
            errors.Add("加载后的镜像缺少正确的 App role label。");
        }

        if (image.RepoTags is null
            || !image.RepoTags.Contains(expectedLoadedTag, StringComparer.Ordinal))
        {
            errors.Add("加载后的镜像缺少 manifest 声明的本地标签。");
        }

        if (errors.Count > 0)
        {
            throw new UpdateOperationException(
                StatusCodes.Status502BadGateway,
                "加载的镜像未通过校验：" + string.Join("；", errors));
        }
    }
}
