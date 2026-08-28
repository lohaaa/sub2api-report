using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.UnitTests.Reports;

public sealed class ReportSchedulingStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReportScheduleHasStableDefaultsAndRevisionedUpdates()
    {
        var schedule = ReportSchedule.CreateDefault();

        Assert.False(schedule.Enabled);
        Assert.Equal(1, schedule.DayOfMonth);
        Assert.Equal("09:00", schedule.LocalTime);
        Assert.Equal("Asia/Shanghai", schedule.Timezone);
        Assert.Equal(1, schedule.Revision);

        schedule.Update(true, 18, "07:05", "UTC", null, Now);

        Assert.True(schedule.Enabled);
        Assert.Equal(18, schedule.DayOfMonth);
        Assert.Equal("07:05", schedule.LocalTime);
        Assert.Equal("UTC", schedule.Timezone);
        Assert.Equal(2, schedule.Revision);
        Assert.Equal(Now, schedule.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void ReportScheduleRejectsDaysThatCannotOccurEveryMonth(int dayOfMonth)
    {
        var schedule = ReportSchedule.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            schedule.Update(true, dayOfMonth, "09:00", "UTC", null, Now));
    }

    [Theory]
    [InlineData("9:00")]
    [InlineData("24:00")]
    [InlineData("09:60")]
    public void ReportScheduleRequiresCanonicalWallClockTime(string localTime)
    {
        var schedule = ReportSchedule.CreateDefault();

        Assert.Throws<ArgumentException>(() =>
            schedule.Update(true, 1, localTime, "UTC", null, Now));
    }

    [Fact]
    public void ScheduledRunPersistsStagesAndCompletesWithoutDeliveryWhenPartial()
    {
        var run = ReportRun.QueueScheduled(
            ReportSchedule.SingletonId,
            3,
            new DateOnly(2026, 8, 26),
            "Asia/Shanghai",
            null,
            null,
            "scheduled:1:2026-08-26",
            Now);

        Assert.Equal(ReportRunStatus.Queued, run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.False(run.IsTaskRetryable);

        run.BeginCollecting(Now.AddMinutes(1));
        run.BeginRendering(Now.AddMinutes(2));
        var reportId = Guid.NewGuid();
        run.AttachSnapshot(reportId);
        run.CompleteWithoutDelivery(
            ReportRunStatus.PartialFailed,
            "partial_report",
            "Synthetic partial report.",
            Now.AddMinutes(3));

        Assert.Equal(ReportRunStatus.PartialFailed, run.Status);
        Assert.Equal(reportId, run.ReportSnapshotId);
        Assert.Equal("partial_report", run.ErrorCode);
        Assert.NotNull(run.CollectingAt);
        Assert.NotNull(run.RenderingAt);
        Assert.NotNull(run.CompletedAt);
        Assert.True(run.IsTaskRetryable);
    }

    [Fact]
    public void TaskRetryCreatesANewLinkedAttemptWithoutChangingSource()
    {
        var source = ReportRun.QueueManualScheduled(
            ReportSchedule.SingletonId,
            4,
            new DateOnly(2026, 8, 26),
            "UTC",
            null,
            null,
            Now);
        source.BeginCollecting(Now.AddMinutes(1));
        source.Fail("upstream_unavailable", null, Now.AddMinutes(2));

        var retry = ReportRun.QueueRetry(source, false, Now.AddMinutes(5));

        Assert.NotEqual(source.Id, retry.Id);
        Assert.Equal(source.Id, retry.RetryOfRunId);
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(ReportRunTrigger.Retry, retry.Trigger);
        Assert.Equal(ReportRunStatus.Queued, retry.Status);
        Assert.Equal(ReportRunStatus.Failed, source.Status);
        Assert.Null(retry.IdempotencyKey);
    }

    [Fact]
    public void ActiveTaskCannotBeRetried()
    {
        var source = ReportRun.QueueManualScheduled(
            ReportSchedule.SingletonId,
            1,
            new DateOnly(2026, 8, 26),
            "UTC",
            null,
            null,
            Now);

        Assert.Throws<InvalidOperationException>(() =>
            ReportRun.QueueRetry(source, false, Now.AddMinutes(1)));
    }
}
