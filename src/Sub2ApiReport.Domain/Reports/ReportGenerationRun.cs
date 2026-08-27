namespace Sub2ApiReport.Domain.Reports;

public enum ReportGenerationStatus
{
    Running,
    Succeeded,
    Failed,
}

public sealed class ReportGenerationRun
{
    private ReportGenerationRun()
    {
    }

    public Guid Id { get; private set; }

    public ReportTrigger Trigger { get; private set; }

    public ReportGenerationStatus Status { get; private set; }

    public string? Stage { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public long ConnectionRevision { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public long StartedAtUnixMilliseconds { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? ReportSnapshotId { get; private set; }

    public static ReportGenerationRun Start(
        ReportTrigger trigger,
        long connectionRevision,
        DateTimeOffset startedAt) => new()
        {
            Id = Guid.NewGuid(),
            Trigger = trigger,
            Status = ReportGenerationStatus.Running,
            ConnectionRevision = connectionRevision,
            StartedAt = startedAt,
            StartedAtUnixMilliseconds = startedAt.ToUnixTimeMilliseconds(),
        };

    public bool MarkConnectionRevision(long connectionRevision)
    {
        if (Status != ReportGenerationStatus.Running || connectionRevision <= 0)
        {
            return false;
        }

        ConnectionRevision = connectionRevision;
        return true;
    }

    public bool MarkSucceeded(Guid reportSnapshotId, DateTimeOffset completedAt)
    {
        if (Status != ReportGenerationStatus.Running)
        {
            return false;
        }

        Status = ReportGenerationStatus.Succeeded;
        ReportSnapshotId = reportSnapshotId;
        CompletedAt = completedAt;
        return true;
    }

    public bool MarkFailed(
        string stage,
        string? errorCode,
        string errorMessage,
        long connectionRevision,
        DateTimeOffset completedAt)
    {
        if (Status != ReportGenerationStatus.Running)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Status = ReportGenerationStatus.Failed;
        Stage = stage;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ConnectionRevision = connectionRevision;
        CompletedAt = completedAt;
        return true;
    }
}
