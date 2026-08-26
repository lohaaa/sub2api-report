using Sub2ApiReport.Domain.Sub2Api;

namespace Sub2ApiReport.UnitTests.Sub2Api;

public sealed class Sub2ApiDomainTests
{
    [Fact]
    public void ConnectionTracksRevisionAndCanClearSecret()
    {
        var now = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var connection = Sub2ApiConnection.Create(
            "https://sub2api.example.com",
            "protected-value",
            "1234",
            42,
            7,
            now);

        connection.Update(
            "https://sub2api.example.com/v2",
            null,
            null,
            clearAdminApiKey: true,
            43,
            null,
            now.AddMinutes(1));

        Assert.Null(connection.AdminApiKeyCiphertext);
        Assert.Null(connection.AdminApiKeySuffix);
        Assert.Equal(2, connection.Revision);
    }

    [Fact]
    public void ExternalKeyUpdatesAndReactivatesWithoutChangingIdentity()
    {
        var firstSeen = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var key = ExternalApiKey.Create(101, "Original", "active", 7, null, firstSeen);
        var id = key.Id;
        Assert.True(key.MarkRetired(firstSeen.AddDays(1)));

        var changed = key.ApplySnapshot(
            "Renamed",
            "inactive",
            7,
            firstSeen.AddHours(3),
            firstSeen.AddDays(2));

        Assert.True(changed);
        Assert.Equal(id, key.Id);
        Assert.Equal("Renamed", key.NameSnapshot);
        Assert.Null(key.RetiredAt);
    }
}
