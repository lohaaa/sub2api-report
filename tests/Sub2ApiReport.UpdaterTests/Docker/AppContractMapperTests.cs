using System.Text.Json;
using System.Text.Json.Serialization;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Http;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
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
        Assert.Equal(snapshot.Env.Count + 1, parameters.Env.Count);
        Assert.All(snapshot.Env, value => Assert.Contains(value, parameters.Env));
        Assert.Contains(
            $"{UpdateContractConstants.MaintenanceOperationEnvironmentKey}={operationId}",
            parameters.Env);
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
        // named volume 重放使用 volume 名称，而不是宿主机数据路径。
        Assert.Equal(
            "sub2api-report-data",
            parameters.HostConfig!.Mounts![0].Source);
    }

    [Fact]
    public void ToCreateParametersExcludesEffectiveMountsCoveredByBinds()
    {
        var snapshot = AppContractMapper.MapSnapshot(CreateOfficialComposeInspect());

        var parameters = AppContractMapper.ToCreateParameters(
            snapshot,
            "sha256:" + TestReleases.Hex('1'),
            "operation-official");

        AssertOfficialComposeCreateParameters(parameters);
        // bind 目标只由 Binds 原字符串承载，不再出现在有效 Mounts 重放中。
        Assert.DoesNotContain(
            parameters.HostConfig!.Mounts!,
            mount => mount.Target == "/run/secrets/updater-token");
    }

    [Fact]
    public void ToCreateParametersHandlesPersistedRollbackSnapshot()
    {
        // 回滚重建重建使用持久化 JSON 快照，与直接映射的快照必须产生相同创建参数。
        var snapshot = AppContractMapper.MapSnapshot(CreateOfficialComposeInspect());
        var persisted = JsonSerializer.Serialize(snapshot, RollbackStateOptions);

        var restored = JsonSerializer.Deserialize<AppContainerSnapshot>(persisted, RollbackStateOptions);

        Assert.NotNull(restored);
        AssertOfficialComposeCreateParameters(
            AppContractMapper.ToCreateParameters(restored!, restored!.ImageId, "operation-rollback"));
    }

    [Fact]
    public void ToCreateParametersKeepsMountsExpressedOnlyViaMountSyntax()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Mounts =
            [
                new AppMount("bind", null, "/srv/sub2api-report/extra-conf", "/app/extra-config", true),
                new AppMount("tmpfs", null, null, "/cache", false),
            ],
        };

        var parameters = AppContractMapper.ToCreateParameters(
            snapshot,
            "sha256:" + TestReleases.Hex('2'),
            "operation-mount-only");

        Assert.Empty(parameters.HostConfig!.Binds!);
        Assert.Equal(2, parameters.HostConfig.Mounts!.Count);
        var bindMount = parameters.HostConfig.Mounts.Single(mount => mount.Type == "bind");
        Assert.Equal("/app/extra-config", bindMount.Target);
        Assert.Equal("/srv/sub2api-report/extra-conf", bindMount.Source);
        Assert.True(bindMount.ReadOnly);
        var tmpfsMount = parameters.HostConfig.Mounts.Single(mount => mount.Type == "tmpfs");
        Assert.Equal("/cache", tmpfsMount.Target);
        Assert.False(tmpfsMount.ReadOnly);
    }

    [Fact]
    public void ToCreateParametersExcludesNamedVolumeCoveredByShortSyntaxBinds()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Binds =
            [
                "sub2api-report_app-data:/data",
                "/opt/sub2api-report/secrets/updater-token:/run/secrets/updater-token:ro",
            ],
            Mounts =
            [
                new AppMount(
                    "volume",
                    "sub2api-report_app-data",
                    "/var/lib/docker/volumes/sub2api-report_app-data/_data",
                    UpdateContractConstants.AppDataMountTarget,
                    false),
                new AppMount(
                    "bind",
                    null,
                    "/opt/sub2api-report/secrets/updater-token",
                    "/run/secrets/updater-token",
                    true),
            ],
        };

        var parameters = AppContractMapper.ToCreateParameters(
            snapshot,
            "sha256:" + TestReleases.Hex('3'),
            "operation-short-syntax");

        Assert.Equal(
            ShortSyntaxBinds,
            parameters.HostConfig!.Binds);
        // 两个目标均已由 Binds 原样覆盖，不再通过 Mounts 重复下发。
        Assert.Empty(parameters.HostConfig.Mounts!);
    }

    [Fact]
    public void ToCreateParametersRejectsUnparseableBinds()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Binds = new[] { "/host/a/data:/data:/host/c" },
        };

        var exception = Assert.Throws<UpdateOperationException>(
            () => AppContractMapper.ToCreateParameters(
                snapshot, "sha256:" + TestReleases.Hex('4'), "operation-unsafe"));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Contains("无法安全解析", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/host/a/data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCreateParametersRejectsDuplicateBindTargets()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Binds = new[] { "/x-1:/data:ro", "/x-2:/data:rw" },
        };

        var exception = Assert.Throws<UpdateOperationException>(
            () => AppContractMapper.ToCreateParameters(
                snapshot, "sha256:" + TestReleases.Hex('5'), "operation-dup-bind"));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Contains("目标重复", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/x-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCreateParametersRejectsDuplicateMountTargets()
    {
        var snapshot = TestSnapshots.CreateValidSnapshot() with
        {
            Mounts =
            [
                new AppMount("volume", "vol-a", null, UpdateContractConstants.AppDataMountTarget, false),
                new AppMount("volume", "vol-b", null, UpdateContractConstants.AppDataMountTarget, false),
            ],
        };

        var exception = Assert.Throws<UpdateOperationException>(
            () => AppContractMapper.ToCreateParameters(
                snapshot, "sha256:" + TestReleases.Hex('6'), "operation-dup-mount"));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Contains("重复声明", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/src:/dst", "/dst")]
    [InlineData("/src:/dst:ro", "/dst")]
    [InlineData("/src:/dst:ro,rprivate,rbind", "/dst")]
    [InlineData("/src:/dst:ro:z", "/dst")]
    [InlineData("sub2api-report_app-data:/data", "/data")]
    [InlineData("/data", "/data")]
    [InlineData("/src:/dst/", "/dst/")]
    public void TryParseBindTargetParsesLinuxBindSyntax(string bind, string expectedTarget)
    {
        Assert.True(AppContractMapper.TryParseBindTarget(bind, out var target));
        Assert.Equal(expectedTarget, target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:/x:/y:ro")]
    [InlineData("/a:/b:/c")]
    [InlineData("relative")]
    [InlineData("src:relative")]
    public void TryParseBindTargetRejectsUnsafeSyntax(string bind)
    {
        Assert.False(AppContractMapper.TryParseBindTarget(bind, out _));
    }

    private static readonly JsonSerializerOptions RollbackStateOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>官方 Compose 契约：named volume 数据卷加只读 updater token bind。</summary>
    private static readonly string[] OfficialBinds =
    [
        "/opt/sub2api-report/secrets/updater-token:/run/secrets/updater-token:ro",
    ];

    private static readonly string[] ShortSyntaxBinds =
    [
        "sub2api-report_app-data:/data",
        "/opt/sub2api-report/secrets/updater-token:/run/secrets/updater-token:ro",
    ];

    /// <summary>官方 Compose 契约：named volume 数据卷加只读 updater token bind。</summary>
    private static void AssertOfficialComposeCreateParameters(CreateContainerParameters parameters)
    {
        Assert.Equal(OfficialBinds, parameters.HostConfig!.Binds);
        var mount = Assert.Single(parameters.HostConfig.Mounts!);
        Assert.Equal("volume", mount.Type);
        Assert.Equal(UpdateContractConstants.AppDataMountTarget, mount.Target);
        Assert.Equal("sub2api-report_app-data", mount.Source);
        Assert.False(mount.ReadOnly);
    }

    private static ContainerInspectResponse CreateOfficialComposeInspect()
    {
        var inspect = CreateValidInspect();
        inspect.HostConfig!.Binds = OfficialBinds;
        inspect.Mounts =
        [
            new MountPoint
            {
                Type = "volume",
                Name = "sub2api-report_app-data",
                Source = "/var/lib/docker/volumes/sub2api-report_app-data/_data",
                Destination = UpdateContractConstants.AppDataMountTarget,
                RW = true,
            },
            new MountPoint
            {
                Type = "bind",
                Source = "/opt/sub2api-report/secrets/updater-token",
                Destination = "/run/secrets/updater-token",
                RW = false,
            },
        ];
        return inspect;
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
                [UpdateContractConstants.ContractLabelKey] =
                    UpdateContractConstants.DeploymentContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
