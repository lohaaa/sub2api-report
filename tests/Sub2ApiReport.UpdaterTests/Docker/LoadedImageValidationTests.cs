using Docker.DotNet.Models;
using Sub2ApiReport.UpdateContracts;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Docker;

namespace Sub2ApiReport.UpdaterTests.Docker;

public sealed class LoadedImageValidationTests
{
    private const string LoadedTag = "sub2api-report-app:1.2.0";
    private const string Version = "1.2.0";
    private static readonly string ConfigDigest = "sha256:" + TestReleases.Hex('a');
    private static readonly string TargetDigest = "sha256:" + TestReleases.Hex('b');

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptsClassicConfigOrContainerdTargetDigest(bool useTargetDigest)
    {
        var image = CreateValidImage(useTargetDigest ? TargetDigest : ConfigDigest);

        DockerAppManager.ValidateLoadedImage(
            image,
            ConfigDigest,
            TargetDigest,
            LoadedTag,
            Version);
    }

    [Fact]
    public void RejectsIdOutsideSignedConfigAndTargetDigests()
    {
        var image = CreateValidImage("sha256:" + TestReleases.Hex('c'));

        var exception = Assert.Throws<UpdateOperationException>(() =>
            DockerAppManager.ValidateLoadedImage(
                image,
                ConfigDigest,
                TargetDigest,
                LoadedTag,
                Version));

        Assert.Contains("config/target digest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWrongImageRole()
    {
        var image = CreateValidImage(ConfigDigest);
        image.Config.Labels[UpdateContractConstants.AppRoleLabelKey] = "updater";

        var exception = Assert.Throws<UpdateOperationException>(() =>
            DockerAppManager.ValidateLoadedImage(
                image,
                ConfigDigest,
                TargetDigest,
                LoadedTag,
                Version));

        Assert.Contains("role label", exception.Message, StringComparison.Ordinal);
    }

    private static ImageInspectResponse CreateValidImage(string id) => new()
    {
        ID = id,
        Os = "linux",
        Architecture = "amd64",
        RepoTags = [LoadedTag],
        Config = new Config
        {
            Labels = new Dictionary<string, string>
            {
                [UpdateContractConstants.ImageVersionLabelKey] = Version,
                [UpdateContractConstants.AppRoleLabelKey] = UpdateContractConstants.AppRoleLabelValue,
            },
        },
    };
}
