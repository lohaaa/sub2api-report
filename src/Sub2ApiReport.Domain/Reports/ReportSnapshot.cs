namespace Sub2ApiReport.Domain.Reports;

public enum ReportStatus
{
    Complete,
    Partial,
}

public enum ReportTrigger
{
    ManualDryRun,
}

public sealed class ReportSnapshot
{
    public const int CurrentSchemaVersion = 3;

    private ReportSnapshot()
    {
    }

    public Guid Id { get; private init; }

    public int SchemaVersion { get; private init; }

    public ReportStatus Status { get; private init; }

    public ReportTrigger Trigger { get; private init; }

    public DateOnly CutoffDate { get; private init; }

    public string Timezone { get; private init; } = string.Empty;

    public long ConnectionRevision { get; private init; }

    public DateTimeOffset GeneratedAt { get; private init; }

    public long GeneratedAtUnixMilliseconds { get; private init; }

    public int UserCount { get; private init; }

    public int KeyCount { get; private init; }

    public int FailedRangeCount { get; private init; }

    public decimal SevenDayActualCost { get; private init; }

    public decimal ThirtyDayActualCost { get; private init; }

    public string CanonicalJson { get; private init; } = string.Empty;

    public static ReportSnapshot Create(
        Guid id,
        ReportStatus status,
        ReportTrigger trigger,
        DateOnly cutoffDate,
        string timezone,
        long connectionRevision,
        DateTimeOffset generatedAt,
        int userCount,
        int keyCount,
        int failedRangeCount,
        decimal sevenDayActualCost,
        decimal thirtyDayActualCost,
        string canonicalJson)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The report identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        if (timezone.Length > 100 || connectionRevision <= 0)
        {
            throw new ArgumentException("The report configuration snapshot is invalid.");
        }

        if (userCount < 0 || keyCount < 0 || failedRangeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userCount), "Report counts cannot be negative.");
        }

        if (sevenDayActualCost < 0 || thirtyDayActualCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sevenDayActualCost), "Report costs cannot be negative.");
        }

        return new ReportSnapshot
        {
            Id = id,
            SchemaVersion = CurrentSchemaVersion,
            Status = status,
            Trigger = trigger,
            CutoffDate = cutoffDate,
            Timezone = timezone,
            ConnectionRevision = connectionRevision,
            GeneratedAt = generatedAt,
            GeneratedAtUnixMilliseconds = generatedAt.ToUnixTimeMilliseconds(),
            UserCount = userCount,
            KeyCount = keyCount,
            FailedRangeCount = failedRangeCount,
            SevenDayActualCost = sevenDayActualCost,
            ThirtyDayActualCost = thirtyDayActualCost,
            CanonicalJson = canonicalJson,
        };
    }
}
