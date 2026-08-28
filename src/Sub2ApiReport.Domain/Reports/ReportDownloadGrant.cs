namespace Sub2ApiReport.Domain.Reports;

public sealed class ReportDownloadGrant
{
    public const int TokenHashLength = 64;
    public const int TokenCiphertextMaxLength = 4096;

    private ReportDownloadGrant()
    {
    }

    public Guid Id { get; private init; }

    public Guid DeliveryId { get; private init; }

    public DeliveryRecord Delivery { get; private init; } = null!;

    public Guid ReportSnapshotId { get; private init; }

    public ReportSnapshot ReportSnapshot { get; private init; } = null!;

    public string TokenHash { get; private set; } = string.Empty;

    public string TokenCiphertext { get; private set; } = string.Empty;

    public int LifetimeHours { get; private set; }

    public int? MaxDownloads { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public int DownloadCount { get; private set; }

    public DateTimeOffset? LastDownloadedAt { get; private set; }

    public bool IsPending => ExpiresAt is null && RevokedAt is null;

    public bool IsAvailable(DateTimeOffset now) =>
        RevokedAt is null
        && ExpiresAt is { } expiresAt
        && expiresAt > now
        && (MaxDownloads is null || DownloadCount < MaxDownloads);

    public static ReportDownloadGrant Create(
        Guid deliveryId,
        Guid reportSnapshotId,
        string tokenHash,
        string tokenCiphertext,
        int lifetimeHours,
        int? maxDownloads,
        DateTimeOffset createdAt)
    {
        if (deliveryId == Guid.Empty || reportSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("The delivery and report identifiers are required.");
        }

        ValidateToken(tokenHash, tokenCiphertext);
        ValidateLifetime(lifetimeHours);
        ValidateMaxDownloads(maxDownloads);
        return new ReportDownloadGrant
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            ReportSnapshotId = reportSnapshotId,
            TokenHash = tokenHash,
            TokenCiphertext = tokenCiphertext,
            LifetimeHours = lifetimeHours,
            MaxDownloads = maxDownloads,
            CreatedAt = createdAt,
        };
    }

    public void Rotate(
        string tokenHash,
        string tokenCiphertext,
        int lifetimeHours,
        int? maxDownloads,
        DateTimeOffset createdAt)
    {
        ValidateToken(tokenHash, tokenCiphertext);
        ValidateLifetime(lifetimeHours);
        ValidateMaxDownloads(maxDownloads);
        TokenHash = tokenHash;
        TokenCiphertext = tokenCiphertext;
        LifetimeHours = lifetimeHours;
        MaxDownloads = maxDownloads;
        CreatedAt = createdAt;
        ExpiresAt = null;
        RevokedAt = null;
        DownloadCount = 0;
        LastDownloadedAt = null;
    }

    public void Activate(DateTimeOffset activatedAt)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("A revoked download grant cannot be activated.");
        }

        ExpiresAt ??= activatedAt.AddHours(LifetimeHours);
    }

    public void RecordDownload(DateTimeOffset downloadedAt)
    {
        if (!IsAvailable(downloadedAt))
        {
            throw new InvalidOperationException("The report download grant is not active.");
        }

        DownloadCount = checked(DownloadCount + 1);
        LastDownloadedAt = downloadedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt ??= revokedAt;
    }

    private static void ValidateToken(string tokenHash, string tokenCiphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenCiphertext);
        if (tokenHash.Length != TokenHashLength || tokenCiphertext.Length > TokenCiphertextMaxLength)
        {
            throw new ArgumentException("The report download token metadata is invalid.");
        }
    }

    private static void ValidateLifetime(int lifetimeHours)
    {
        if (lifetimeHours is < 1 or > global::Sub2ApiReport.Domain.System.SystemSetting.MaximumReportDownloadLinkHours)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetimeHours));
        }
    }

    private static void ValidateMaxDownloads(int? maxDownloads)
    {
        if (maxDownloads is < 1 or > global::Sub2ApiReport.Domain.System.SystemSetting.MaximumReportDownloadCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDownloads));
        }
    }
}
