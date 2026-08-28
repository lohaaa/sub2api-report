using Docker.DotNet.Models;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater.Docker;

namespace Sub2ApiReport.UpdaterTests.Docker;

public sealed class AppContractMapperTests
{
    [Fact]
    public void MapSnapshotCapturesCurrentDeploymentContract()
    {
        var inspect = CreateValidInspect();

        var snapshot = AppContractMapper.MapSnapshot(inspect);

        Assert.Equal("test-container-1", snapshot.ContainerId);
        Assert.Equal("sub2api-report-app-1", snapshot.ContainerName);
        Assert.Equal("sha256:" + TestReleases.Hex('0'), snapshot.ImageId);
        Assert.Equal("sub2api-report-app:0.7.0", snapshot.CurrentImageTag);
        Assert.Equal(["ASPNETCORE_URLS=http://+:8080"], snapshot.Env);
        Assert.Equal(UpdateContractConstants.AppDataMountTarget, snapshot.Mounts[0].Target);
        Assert.Equal("sub2api-report-data", snapshot.Mounts[0].Name);
        Assert.Equal(["sub2api-report_default"], snapshot.Networks.Select(network => network.Network));
        Assert.Equal(["app"], snapshot.Networks[0].Aliases);
        Assert.Equal("UnlessStopped", snapshot.RestartPolicy!.Name);
        Assert.Equal(["no-new-privileges"], snapshot.SecurityOptions);
        Assert.Equal(2_000_000_000L, snapshot.Resources.NanoCpus);
        Assert.Equal("8080/tcp", snapshot.PortBindings[0].ContainerPort);
        Assert.NotNull(snapshot.Healthcheck);
        Assert.True(snapshot.ExposedPorts.ContainsKey("8080/tcp"));
    }

    [Fact]
    public void ValidateContractAcceptsValidSnapshot()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot();

        Assert.Empty(AppContractMapper.ValidateContract(snapshot));
    }

    [Fact]
    public void ValidateContractRejectsMissingContractElements()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Mounts = [],
            Networks = [],
            Healthcheck = null,
            RestartPolicy = null,
            ExposedPorts = new Dictionary<string, bool>(),
            ImageId = string.Empty,
        };

        var errors = AppContractMapper.ValidateContract(snapshot);

        Assert.Contains(errors, error => error.Contains("数据卷", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("网络", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("健康检查", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("重启策略", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("端口", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("镜像", StringComparison.Ordinal));
    }

    [Fact]
    public void ToCreateParametersPreservesContractAndAddsOperationLabel()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot();
        var operationId = "op123";

        var parameters = AppContractMapper.ToCreateParameters(snapshot, "sha256:" + TestReleases.Hex('9'), operationId);

        Assert.Equal(snapshot.ContainerName, parameters.Name);
        Assert.Equal("sha256:" + TestReleases.Hex('9'), parameters.Image);
        Assert.Equal(operationId, parameters.Labels![UpdateContractConstants.UpgradeOperationLabelKey]);
        Assert.Equal(snapshot.Env, parameters.Env);
        Assert.Equal(snapshot.WorkingDir, parameters.WorkingDir);
        Assert.Equal(snapshot.Entrypoint, parameters.Entrypoint);
        Assert.Equal("8080/tcp", parameters.ExposedPorts!.Keys.Single());
        Assert.Equal("8080/tcp", parameters.HostConfig!.PortBindings.Keys.Single());
        Assert.Equal(
            UpdateContractConstants.AppDataMountTarget,
            parameters.HostConfig.Mounts![0].Target);
        Assert.Equal("UnlessStopped", parameters.HostConfig.RestartPolicy!.Name.ToString());
        Assert.Equal(["no-new-privileges"], parameters.HostConfig.SecurityOpt);
        Assert.Equal("sub2api-report_default", parameters.NetworkingConfig!.EndpointsConfig.Keys.Single());
        Assert.Equal(["app"], parameters.NetworkingConfig.EndpointsConfig["sub2api-report_default"].Aliases);
        Assert.NotNull(parameters.Healthcheck);
        Assert.Equal(
            snapshot.Healthcheck!.Test,
            parameters.Healthcheck.Test);
        Assert.Equal(snapshot.Resources.NanoCpus, parameters.HostConfig.NanoCPUs);
        // 原有 role/instance 标签保留。
        Assert.Equal(
            TestSnapshots.CreateValidSnapshot().Labels[UpdateContractConstants.AppRoleLabelKey],
            parameters.Labels[UpdateContractConstants.AppRoleLabelKey]);
    }

    private static ContainerInspectResponse CreateValidInspect() => new()
    {
        ID = "test-container-1",
        Name = "/sub2api-report-app-1",
        Config = new Config
        {
            Image = "sub2api-report-app:0.7.0",
            Env = ["ASPNETCORE_URLS=http://+:8080"],
            Labels = new Dictionary<string, string>
            {
                [UpdateContractConstants.AppRoleLabelKey] = UpdateContractConstants.AppRoleLabelValue,
                [UpdateContractConstants.InstanceLabelKey] = "test-instance",
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default,
            },
            WorkingDir = "/app",
            Entrypoint = ["/app/entrypoint.sh"],
            StopSignal = "SIGTERM",
            StopTimeout = TimeSpan.FromSeconds(10),
            Healthcheck = new HealthConfig
            {
                Test = ["CMD", "wget", "-q", "--spider", "http://localhost:8080/health/live"],
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                StartPeriod = 30,
                Retries = 3,
            },
        },
        Image = "sha256:" + TestReleases.Hex('0'),
        Mounts =
        [
            new MountPoint
            {
                Type = "volume",
                Name = "sub2api-report-data",
                Destination = UpdateContractConstants.AppDataMountTarget,
                RW = true,
            },
        ],
        HostConfig = new HostConfig
        {
            PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                ["8080/tcp"] = [new PortBinding { HostIP = null, HostPort = null }],
            },
            RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
            SecurityOpt = ["no-new-privileges"],
            NanoCPUs = 2_000_000_000,
            Memory = 536870912,
            ExtraHosts = [],
        },
        NetworkSettings = new NetworkSettings
        {
            Networks = new Dictionary<string, EndpointSettings>
            {
                ["sub2api-report_default"] = new EndpointSettings { Aliases = ["app"] },
            },
        },
    };
}
