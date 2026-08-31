using Quartz;
using Quartz.Impl.Triggers;

namespace Sub2ApiReport.IntegrationTests;

/// <summary>
/// Minimal deterministic <see cref="IJobExecutionContext"/> for exercising the persistent
/// schedule trigger gate without running the whole scheduler pipeline. Only the trigger key
/// and scheduled fire time carry meaning for the gate; the cron payload is inert.
/// </summary>
internal sealed class TestJobExecutionContext(TriggerKey triggerKey, DateTimeOffset scheduledFireTimeUtc)
    : IJobExecutionContext
{
    public IScheduler Scheduler => throw new NotSupportedException();
    public ITrigger Trigger { get; } = new CronTriggerImpl(
        triggerKey.Name,
        triggerKey.Group,
        "0 30 9 31 * ?")
    {
        StartTimeUtc = scheduledFireTimeUtc.AddYears(-1),
    };
    public ICalendar Calendar => throw new NotSupportedException();
    public bool Recovering => false;
    public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();
    public int RefireCount => 0;
    public JobDataMap MergedJobDataMap { get; } = new();
    public IJobDetail JobDetail => throw new NotSupportedException();
    public IJob JobInstance => throw new NotSupportedException();
    public DateTimeOffset FireTimeUtc => scheduledFireTimeUtc;
    public DateTimeOffset? ScheduledFireTimeUtc { get; set; } = scheduledFireTimeUtc;
    public DateTimeOffset? PreviousFireTimeUtc { get; set; }
    public DateTimeOffset? NextFireTimeUtc { get; set; }
    public object? Result { get; set; }
    public TimeSpan JobRunTime => TimeSpan.Zero;
    public string FireInstanceId => throw new NotSupportedException();
    public CancellationToken CancellationToken => CancellationToken.None;

    public void Put(object key, object? value) { }
    public object? Get(object key) => null;
}
