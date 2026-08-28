using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.UnitTests.Reports;

public sealed class ReportDownloadGrantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GrantActivatesOnSuccessfulDeliveryAndEnforcesDownloadLimit()
    {
        var grant = CreateGrant(maxDownloads: 2);

        Assert.True(grant.IsPending);
        Assert.False(grant.IsAvailable(Now));

        grant.Activate(Now);
        Assert.Equal(Now.AddHours(24), grant.ExpiresAt);
        Assert.True(grant.IsAvailable(Now));

        grant.RecordDownload(Now.AddMinutes(1));
        Assert.True(grant.IsAvailable(Now.AddMinutes(1)));
        grant.RecordDownload(Now.AddMinutes(2));

        Assert.Equal(2, grant.DownloadCount);
        Assert.False(grant.IsAvailable(Now.AddMinutes(2)));
    }

    [Fact]
    public void UnlimitedGrantExpiresAndCanBeRevokedEarly()
    {
        var grant = CreateGrant(maxDownloads: null);
        grant.Activate(Now);
        grant.RecordDownload(Now.AddHours(1));

        Assert.True(grant.IsAvailable(Now.AddHours(23)));
        Assert.False(grant.IsAvailable(Now.AddHours(24)));

        grant.Revoke(Now.AddHours(2));
        Assert.False(grant.IsAvailable(Now.AddHours(2)));
    }

    [Fact]
    public void RotationFreezesNewPolicyAndClearsPreviousUsage()
    {
        var grant = CreateGrant(maxDownloads: 1);
        grant.Activate(Now);
        grant.RecordDownload(Now.AddMinutes(1));
        grant.Revoke(Now.AddMinutes(2));

        grant.Rotate(new string('b', 64), "ciphertext-2", 48, null, Now.AddHours(3));

        Assert.True(grant.IsPending);
        Assert.Equal(48, grant.LifetimeHours);
        Assert.Null(grant.MaxDownloads);
        Assert.Equal(0, grant.DownloadCount);
        Assert.Null(grant.RevokedAt);
    }

    private static ReportDownloadGrant CreateGrant(int? maxDownloads) =>
        ReportDownloadGrant.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new string('a', 64),
            "ciphertext-1",
            24,
            maxDownloads,
            Now);
}
