using System.Security.Cryptography;
using Sub2ApiReport.Domain.Security;

namespace Sub2ApiReport.UnitTests.Security;

public sealed class SecurityChallengeTests
{
    private static readonly byte[] CodeHash = SHA256.HashData([1, 2, 3, 4]);

    [Fact]
    public void SetupChallengeLocksAfterFiveFailures()
    {
        var now = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var challenge = SetupChallenge.Create(CodeHash, now);

        for (var attempt = 0; attempt < SetupChallenge.MaximumFailedAttempts; attempt++)
        {
            challenge.RegisterFailure(now);
        }

        Assert.True(challenge.IsLocked(now));
        Assert.False(challenge.IsAvailable(now));
        Assert.Equal(now.Add(SetupChallenge.LockoutDuration), challenge.LockedUntil);
    }

    [Fact]
    public void ConsumedRecoveryChallengeCannotBeReused()
    {
        var now = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var challenge = RecoveryChallenge.Create(Guid.NewGuid(), CodeHash, now);

        challenge.Consume(now);

        Assert.False(challenge.IsAvailable(now));
        Assert.Throws<InvalidOperationException>(() => challenge.Consume(now));
    }
}
