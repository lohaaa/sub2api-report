using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Scheduling;

namespace Sub2ApiReport.IntegrationTests;

public sealed class QuartzShortMonthSchedulingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 3, 10, 1, 0, 0, TimeSpan.Zero);

    private static ReportScheduleSnapshot Snapshot(
        bool enabled = true,
        int dayOfMonth = 31,
        ShortMonthStrategy strategy = ShortMonthStrategy.UseLastDay,
        string timezone = "Asia/Shanghai") => new(
        ReportSchedule.SingletonId,
        enabled,
        dayOfMonth,
        strategy,
        "09:30",
        timezone,
        null,
        1,
        FixedNow);

    private static ReportSchedule StoredSchedule(
        int dayOfMonth,
        ShortMonthStrategy strategy,
        string timezone = "Asia/Shanghai")
    {
        var schedule = ReportSchedule.CreateDefault();
        schedule.Update(true, dayOfMonth, strategy, "09:30", timezone, null, FixedNow);
        return schedule;
    }

    private static bool Gate(
        ReportSchedule schedule,
        TriggerKey triggerKey,
        DateTimeOffset scheduledFireTimeUtc) => ScheduledReportJob.ShouldScheduledTriggerExecute(
        schedule,
        new TestJobExecutionContext(triggerKey, scheduledFireTimeUtc));

    [Fact]
    public void ScheduledTriggerWithoutRunIdIsTreatedAsAutomatic()
    {
        Assert.Null(ScheduledReportJob.GetConfiguredRunId(new JobDataMap()));
    }

    [Fact]
    public void ImmediateTriggerUsesConfiguredRunId()
    {
        var expected = Guid.NewGuid();
        var data = new JobDataMap();
        data[QuartzReportScheduleCoordinator.RunIdKey] = expected.ToString("D");

        Assert.Equal(expected, ScheduledReportJob.GetConfiguredRunId(data));
    }

    [Fact]
    public void InvalidConfiguredRunIdIsRejected()
    {
        var data = new JobDataMap();
        data[QuartzReportScheduleCoordinator.RunIdKey] = "not-a-guid";

        Assert.Throws<JobExecutionException>(() => ScheduledReportJob.GetConfiguredRunId(data));
    }

    [Fact]
    public void DisabledScheduleProducesNoTriggers()
    {
        var triggers = QuartzReportScheduleCoordinator.BuildDesiredTriggers(
            Snapshot(enabled: false));

        Assert.Empty(triggers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void SkipMonthStrategyAlwaysKeepsOnlyThePrimaryTrigger(int dayOfMonth)
    {
        var triggers = QuartzReportScheduleCoordinator.BuildDesiredTriggers(
            Snapshot(dayOfMonth: dayOfMonth, strategy: ShortMonthStrategy.SkipMonth));

        var trigger = Assert.Single(triggers);
        Assert.Equal(QuartzReportScheduleCoordinator.ScheduleTriggerKey, trigger.Key);
        Assert.Equal(
            FormattableString.Invariant($"0 30 9 {dayOfMonth} * ?"),
            Assert.IsAssignableFrom<ICronTrigger>(trigger).CronExpressionString);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public void UseLastDayWithinShortMonthsKeepsOnlyThePrimaryTrigger(int dayOfMonth)
    {
        var triggers = QuartzReportScheduleCoordinator.BuildDesiredTriggers(
            Snapshot(dayOfMonth: dayOfMonth, strategy: ShortMonthStrategy.UseLastDay));

        var trigger = Assert.Single(triggers);
        Assert.Equal(QuartzReportScheduleCoordinator.ScheduleTriggerKey, trigger.Key);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void UseLastDayBeyondDay28BuildsPrimaryAndMonthEndFallback(int dayOfMonth)
    {
        var triggers = QuartzReportScheduleCoordinator.BuildDesiredTriggers(
            Snapshot(dayOfMonth: dayOfMonth, strategy: ShortMonthStrategy.UseLastDay))
            .OrderBy(trigger => trigger.Key.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, triggers.Count);
        var primary = Assert.IsAssignableFrom<ICronTrigger>(triggers.Single(trigger =>
            trigger.Key.Equals(QuartzReportScheduleCoordinator.ScheduleTriggerKey)));
        var fallback = Assert.IsAssignableFrom<ICronTrigger>(triggers.Single(trigger =>
            trigger.Key.Equals(QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey)));
        Assert.Equal(FormattableString.Invariant($"0 30 9 {dayOfMonth} * ?"), primary.CronExpressionString);
        Assert.Equal("0 30 9 L * ?", fallback.CronExpressionString);
        Assert.Equal(
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"),
            primary.TimeZone);
        Assert.Equal(primary.TimeZone, fallback.TimeZone);
        Assert.Equal(MisfireInstruction.CronTrigger.FireOnceNow, primary.MisfireInstruction);
        Assert.Equal(MisfireInstruction.CronTrigger.FireOnceNow, fallback.MisfireInstruction);
        Assert.Equal(
            QuartzReportScheduleCoordinator.ReportJobKey,
            triggers[0].JobKey);
        Assert.Equal(
            QuartzReportScheduleCoordinator.ReportJobKey,
            triggers[1].JobKey);
    }

    [Fact]
    public void PrimaryFiresAndFallbackSkipsWhenTheExactDayIsTheMonthEnd()
    {
        var schedule = StoredSchedule(30, ShortMonthStrategy.UseLastDay);

        // April 30 is both the configured day 30 and the last day of the month:
        // the primary trigger must run exactly once and the fallback must stand down.
        var scheduledFireTime = new DateTimeOffset(2026, 4, 30, 9, 30, 0, TimeSpan.Zero);

        Assert.True(Gate(
            schedule,
            QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            scheduledFireTime));
        Assert.False(Gate(
            schedule,
            QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
            scheduledFireTime));
    }

    [Fact]
    public void FallbackRunsOnlyInMonthsWithoutTheConfiguredDay()
    {
        var schedule = StoredSchedule(31, ShortMonthStrategy.UseLastDay);

        // Non-leap February 2026 has 28 days.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
            new DateTimeOffset(2026, 2, 28, 9, 30, 0, TimeSpan.Zero)));
        // 30-day April has no day 31.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
            new DateTimeOffset(2026, 4, 30, 9, 30, 0, TimeSpan.Zero)));
        // 31-day March has day 31, so the fallback must not run again on the month end.
        Assert.False(Gate(schedule, QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
            new DateTimeOffset(2026, 3, 31, 9, 30, 0, TimeSpan.Zero)));
        // Leap February 2028 still has no day 31.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
            new DateTimeOffset(2028, 2, 29, 9, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void PrimaryRunsOnlyOnTheNaturallyScheduledDay()
    {
        var schedule = StoredSchedule(31, ShortMonthStrategy.UseLastDay);

        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 3, 31, 9, 30, 0, TimeSpan.Zero)));
        Assert.False(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 2, 28, 9, 30, 0, TimeSpan.Zero)));
        Assert.False(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 4, 30, 9, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GateEvaluatesTheScheduledFireTimeInScheduleTimezone()
    {
        var schedule = StoredSchedule(1, ShortMonthStrategy.UseLastDay);

        // Shanghai is UTC+8: both instants resolve to March 1 local, matching day 1.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 2, 28, 20, 30, 0, TimeSpan.Zero)));
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 3, 1, 1, 30, 0, TimeSpan.Zero)));
        // A fire time on Feb 28 local (early UTC) is not day 1.
        Assert.False(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 2, 28, 1, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GateFollowsDstTransitionsInScheduleTimezone()
    {
        var schedule = StoredSchedule(15, ShortMonthStrategy.SkipMonth, "America/New_York");

        // 2026-03-15 09:30 EDT (-04:00) fires as 13:30Z after spring forward.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 3, 15, 13, 30, 0, TimeSpan.Zero)));
        // 2026-11-15 09:30 EST (-05:00) fires as 14:30Z after fall back.
        Assert.True(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 11, 15, 14, 30, 0, TimeSpan.Zero)));
        // The following day in New York must not run for configured day 15.
        Assert.False(Gate(schedule, QuartzReportScheduleCoordinator.ScheduleTriggerKey,
            new DateTimeOffset(2026, 3, 16, 13, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task ApplyCreatesFallbackThenRemovesItWhenDayMovesWithinShortMonths()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {

            var before = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);
            Assert.True(before.Synchronized);
            Assert.NotNull(before.NextRunAt);
            Assert.Equal(2, (await scheduler.GetTriggerKeys(TriggerKeysQuery())).Count);

            var collapsed = await coordinator.ApplyAsync(
                Snapshot(dayOfMonth: 20, strategy: ShortMonthStrategy.SkipMonth),
                CancellationToken.None);
            Assert.True(collapsed.Synchronized);
            var keys = await scheduler.GetTriggerKeys(TriggerKeysQuery());
            var key = Assert.Single(keys);
            Assert.Equal(QuartzReportScheduleCoordinator.ScheduleTriggerKey, key);

            var projection = await coordinator.GetProjectionAsync(
                Snapshot(dayOfMonth: 20, strategy: ShortMonthStrategy.SkipMonth),
                CancellationToken.None);
            Assert.True(projection.Synchronized);
            Assert.Equal(collapsed.NextRunAt, projection.NextRunAt);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProjectionReportsMinimumNextRunOfBothTriggers()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {
            var applied = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);
            Assert.True(applied.Synchronized);

            var projection = await coordinator.GetProjectionAsync(
                Snapshot(),
                CancellationToken.None);
            Assert.True(projection.Synchronized);

            var triggers = await scheduler.GetTriggersOfJob(
                QuartzReportScheduleCoordinator.ReportJobKey,
                CancellationToken.None);
            Assert.Equal(2, triggers.Count);
            var minimum = triggers
                .Select(trigger => trigger.GetNextFireTimeUtc())
                .OfType<DateTimeOffset>()
                .Min();
            Assert.Equal(minimum, projection.NextRunAt);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProjectionFlagsMismatchedTriggerDefinition()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {
            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);

            var healthy = await coordinator.GetProjectionAsync(Snapshot(), CancellationToken.None);
            Assert.True(healthy.Synchronized);

            // Replacing the fallback with a wrong cron definition must be detected.
            _ = await scheduler.RescheduleJob(
                QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
                TriggerBuilder.Create()
                    .WithIdentity(QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey)
                    .ForJob(QuartzReportScheduleCoordinator.ReportJobKey)
                    .WithCronSchedule("0 30 10 L * ?")
                    .Build(),
                CancellationToken.None);
            var mismatched = await coordinator.GetProjectionAsync(Snapshot(), CancellationToken.None);
            Assert.False(mismatched.Synchronized);
            Assert.Equal("trigger_mismatch", mismatched.ErrorCode);

            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);
            var repaired = await coordinator.GetProjectionAsync(Snapshot(), CancellationToken.None);
            Assert.True(repaired.Synchronized);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProjectionFlagsMissingTriggersUntilReapplied()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {
            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);
            _ = await scheduler.UnscheduleJob(
                QuartzReportScheduleCoordinator.ShortMonthFallbackTriggerKey,
                CancellationToken.None);

            var projection = await coordinator.GetProjectionAsync(Snapshot(), CancellationToken.None);
            Assert.False(projection.Synchronized);
            Assert.Equal("trigger_set_mismatch", projection.ErrorCode);

            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);
            var repaired = await coordinator.GetProjectionAsync(Snapshot(), CancellationToken.None);
            Assert.True(repaired.Synchronized);
            Assert.Equal(2, (await scheduler.GetTriggerKeys(TriggerKeysQuery())).Count);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DisabledScheduleRemovesTriggersAndStaysSynchronized()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {
            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);

            var disabled = await coordinator.ApplyAsync(
                Snapshot(enabled: false),
                CancellationToken.None);
            Assert.True(disabled.Synchronized);
            Assert.Null(disabled.NextRunAt);
            Assert.Empty(await scheduler.GetTriggerKeys(TriggerKeysQuery()));

            var healthy = await coordinator.GetProjectionAsync(
                Snapshot(enabled: false),
                CancellationToken.None);
            Assert.True(healthy.Synchronized);

            // A stale trigger left behind while disabled must be reported and cleaned up.
            var stale = TriggerBuilder.Create()
                .WithIdentity(QuartzReportScheduleCoordinator.ScheduleTriggerKey)
                .ForJob(QuartzReportScheduleCoordinator.ReportJobKey)
                .WithCronSchedule("0 30 9 28 * ?")
                .Build();
            _ = await scheduler.ScheduleJob(stale, CancellationToken.None);
            var staleProjection = await coordinator.GetProjectionAsync(
                Snapshot(enabled: false),
                CancellationToken.None);
            Assert.False(staleProjection.Synchronized);
            Assert.Equal("disabled_trigger_present", staleProjection.ErrorCode);

            var reenabled = await coordinator.ApplyAsync(
                Snapshot(dayOfMonth: 20, strategy: ShortMonthStrategy.SkipMonth),
                CancellationToken.None);
            Assert.True(reenabled.Synchronized);
            var reenabledKeys = await scheduler.GetTriggerKeys(TriggerKeysQuery());
            var reenabledKey = Assert.Single(reenabledKeys);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DurableJobKeepsRecoveryAndDisallowConcurrencyAttributes()
    {
        var (scheduler, coordinator) = await CreateIsolatedAsync();
        try
        {
            _ = await coordinator.ApplyAsync(Snapshot(), CancellationToken.None);

            var job = await scheduler.GetJobDetail(
                QuartzReportScheduleCoordinator.ReportJobKey,
                CancellationToken.None);
            Assert.NotNull(job);
            Assert.True(job.Durable);
            Assert.True(job.RequestsRecovery);
        }
        finally
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
    }


    private static GroupMatcher<TriggerKey> TriggerKeysQuery() =>
        GroupMatcher<TriggerKey>.GroupEquals(QuartzReportScheduleCoordinator.TriggerGroup);

    private static async Task<(IScheduler Scheduler, QuartzReportScheduleCoordinator Coordinator)>
        CreateIsolatedAsync()
    {
        var factory = new StdSchedulerFactory(new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = $"short-month-{Guid.NewGuid():N}",
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.threadCount"] = "2",
        });
        var scheduler = await factory.GetScheduler(CancellationToken.None);
        await scheduler.Start(CancellationToken.None);
        var coordinator = new QuartzReportScheduleCoordinator(
            factory,
            NullLogger<QuartzReportScheduleCoordinator>.Instance);
        return (scheduler, coordinator);
    }

}
