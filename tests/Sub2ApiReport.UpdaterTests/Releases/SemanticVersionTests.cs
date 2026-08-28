using Sub2ApiReport.Updater.Releases;

namespace Sub2ApiReport.UpdaterTests.Releases;

public sealed class SemanticVersionTests
{
    [Fact]
    public void ParsesCoreVersion()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3", out var version));
        Assert.Equal("1.2.3", version!.ToString());
        Assert.False(version.HasPrerelease);
        Assert.Null(version.BuildMetadata);
    }

    [Fact]
    public void ParsesPrereleaseAndBuildMetadata()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3-rc.1+build.5", out var version));
        Assert.Equal("rc.1", version!.Prerelease);
        Assert.Equal("build.5", version.BuildMetadata);
        Assert.True(version.HasPrerelease);
        Assert.Equal("1.2.3-rc.1+build.5", version.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-a_b")]
    [InlineData("1.2.3.")]
    [InlineData(" 1.2.3")]
    public void RejectsMalformedVersions(string value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void OrdersVersionsPerSemverSpecification()
    {
        var ordered = new[]
        {
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "1.0.1",
            "1.1.0",
            "2.0.0",
        };

        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = SemanticVersion.Parse(ordered[i - 1]);
            var current = SemanticVersion.Parse(ordered[i]);
            Assert.True(previous < current, $"{ordered[i - 1]} should be lower than {ordered[i]}");
        }
    }

    [Fact]
    public void NumericPrereleaseIdentifierIsLowerThanAlphanumeric()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-1") < SemanticVersion.Parse("1.0.0-alpha"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.1") < SemanticVersion.Parse("1.0.0-alpha.beta"));
    }

    [Fact]
    public void BuildMetadataIsIgnoredInComparison()
    {
        Assert.Equal(0, SemanticVersion.Parse("1.0.0+build.1").CompareTo(SemanticVersion.Parse("1.0.0+build.2")));
        Assert.True(SemanticVersion.Parse("1.0.0+build.1") >= SemanticVersion.Parse("1.0.0"));
        Assert.True(SemanticVersion.Parse("1.0.0+build.1") <= SemanticVersion.Parse("1.0.0"));
    }

    [Fact]
    public void PrereleaseIsLowerThanRelease()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-rc.1") < SemanticVersion.Parse("1.0.0"));
        Assert.True(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.1"));
    }
}
