namespace Sub2ApiReport.Domain.Reports;

public enum ReportRunTrigger
{
    ManualDelivery,
}

public enum ReportRunStatus
{
    Running,
    Succeeded,
    PartialFailed,
    Failed,
}

public sealed class ReportRun
{
    private ReportRun()
    {
    }

    public Guid Id { get; private init; }

    public Guid ReportSnapshotId { get; private init; }

    public ReportRunTrigger Trigger { get; private init; }

    public ReportRunStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private init; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? IdempotencyKey { get; private init; }

    public List<DeliveryRecord> Deliveries { get; private init; } = [];

    public static ReportRun StartManual(Guid reportSnapshotId, DateTimeOffset startedAt)
    {
        if (reportSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("The report snapshot identifier is required.", nameof(reportSnapshotId));
        }

        return new ReportRun
        {
            Id = Guid.NewGuid(),
            ReportSnapshotId = reportSnapshotId,
            Trigger = ReportRunTrigger.ManualDelivery,
            Status = ReportRunStatus.Running,
            StartedAt = startedAt,
        };
    }

    public void Complete(ReportRunStatus status, DateTimeOffset completedAt)
    {
        if (Status != ReportRunStatus.Running)
        {
            throw new InvalidOperationException("A completed run cannot change its status again.");
        }

        if (status is not (ReportRunStatus.Succeeded or ReportRunStatus.PartialFailed
            or ReportRunStatus.Failed))
        {
            throw new ArgumentException(
                "A run can only complete as succeeded, partially failed, or failed.",
                nameof(status));
        }

        Status = status;
        CompletedAt = completedAt;
    }

    public void RecordRetryResult(ReportRunStatus status, DateTimeOffset completedAt)
    {
        if (!IsRetryable)
        {
            throw new InvalidOperationException(
                "Only failed or partially failed runs can record retry results.");
        }

        if (status is not (ReportRunStatus.Succeeded or ReportRunStatus.PartialFailed
            or ReportRunStatus.Failed))
        {
            throw new ArgumentException(
                "A run can only complete as succeeded, partially failed, or failed.",
                nameof(status));
        }

        Status = status;
        CompletedAt = completedAt;
    }

    public bool IsRetryable => Status is ReportRunStatus.PartialFailed or ReportRunStatus.Failed;
}
