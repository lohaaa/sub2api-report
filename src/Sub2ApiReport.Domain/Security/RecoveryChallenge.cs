namespace Sub2ApiReport.Domain.Security;

public sealed class RecoveryChallenge
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private RecoveryChallenge()
    {
    }

    public Guid Id { get; private init; }

    public Guid AdministratorId { get; private init; }

    public byte[] CodeHash { get; private init; } = [];

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static RecoveryChallenge Create(
        Guid administratorId,
        byte[] codeHash,
        DateTimeOffset createdAt)
    {
        if (administratorId == Guid.Empty)
        {
            throw new ArgumentException("Administrator ID is required.", nameof(administratorId));
        }

        ArgumentNullException.ThrowIfNull(codeHash);
        if (codeHash.Length != 32)
        {
            throw new ArgumentException("A SHA-256 code hash is required.", nameof(codeHash));
        }

        return new RecoveryChallenge
        {
            Id = Guid.NewGuid(),
            AdministratorId = administratorId,
            CodeHash = [.. codeHash],
            CreatedAt = createdAt,
            ExpiresAt = createdAt.Add(Lifetime),
        };
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public bool IsAvailable(DateTimeOffset now) =>
        ConsumedAt is null && RevokedAt is null && ExpiresAt > now && !IsLocked(now);

    public void RegisterFailure(DateTimeOffset now)
    {
        if (ConsumedAt is not null || RevokedAt is not null || ExpiresAt <= now)
        {
            return;
        }

        FailedAttempts++;
        if (FailedAttempts < MaximumFailedAttempts)
        {
            return;
        }

        FailedAttempts = 0;
        LockedUntil = now.Add(LockoutDuration);
    }

    public void Consume(DateTimeOffset now)
    {
        if (!IsAvailable(now))
        {
            throw new InvalidOperationException("The recovery challenge is not available.");
        }

        ConsumedAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (ConsumedAt is null && RevokedAt is null)
        {
            RevokedAt = now;
        }
    }
}
