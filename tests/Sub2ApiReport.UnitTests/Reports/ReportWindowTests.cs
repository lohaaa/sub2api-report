using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.UnitTests.Reports;

public sealed class ReportWindowTests
{
    [Fact]
    public void DefaultWindowsResolveAgainstTheCutoffDate()
    {
        var windows = ReportWindows.Resolve(
            ReportWindows.Default,
            new DateOnly(2026, 8, 25),
            allowCustomRange: true);

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                ReportWindows.RollingSevenDaysKey,
                new DateOnly(2026, 8, 19),
                new DateOnly(2026, 8, 26),
                7),
            window => AssertWindow(
                window,
                ReportWindows.RollingThirtyDaysKey,
                new DateOnly(2026, 7, 27),
                new DateOnly(2026, 8, 26),
                30),
            window => AssertWindow(
                window,
                "previous_calendar_week",
                new DateOnly(2026, 8, 17),
                new DateOnly(2026, 8, 24),
                7),
            window => AssertWindow(
                window,
                "previous_calendar_month",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 8, 1),
                31));
        Assert.Equal("上一自然周", windows[2].Label);
        Assert.Equal("上一自然月", windows[3].Label);
        Assert.Equal(
            "上一自然周",
            ReportWindows.GetDisplayLabel(
                ReportWindowKind.PreviousCalendarWeek,
                "上一完整自然周"));
    }

    [Fact]
    public void RollingWindowSupportsAnArbitraryValidatedDayCount()
    {
        var window = Assert.Single(ReportWindows.Resolve(
            [new ReportWindowSpec("rolling_45_days", ReportWindowKind.RollingDays, RollingDays: 45)],
            new DateOnly(2026, 8, 25),
            allowCustomRange: true));

        AssertWindow(
            window,
            "rolling_45_days",
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 26),
            45);
    }

    [Fact]
    public void CustomRangeConvertsItsInclusiveEndToAnExclusiveBoundary()
    {
        var window = Assert.Single(ReportWindows.Resolve(
            [new ReportWindowSpec(
                "custom_august",
                ReportWindowKind.CustomRange,
                CustomStartDate: new DateOnly(2026, 8, 1),
                CustomEndDate: new DateOnly(2026, 8, 25))],
            new DateOnly(2026, 8, 25),
            allowCustomRange: true));

        AssertWindow(
            window,
            "custom_august",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 26),
            25);
    }

    [Fact]
    public void PreviousCalendarWeekCrossesTheIsoWeekYearBoundary()
    {
        var window = Assert.Single(ReportWindows.Resolve(
            [new ReportWindowSpec(
                "previous_iso_week",
                ReportWindowKind.PreviousCalendarWeek,
                WeekStartsOn: DayOfWeek.Monday)],
            new DateOnly(2026, 1, 1),
            allowCustomRange: true));

        AssertWindow(
            window,
            "previous_iso_week",
            new DateOnly(2025, 12, 22),
            new DateOnly(2025, 12, 29),
            7);
    }

    [Fact]
    public void PreviousCalendarMonthIncludesLeapDay()
    {
        var window = Assert.Single(ReportWindows.Resolve(
            [new ReportWindowSpec("previous_month", ReportWindowKind.PreviousCalendarMonth)],
            new DateOnly(2024, 3, 1),
            allowCustomRange: true));

        AssertWindow(
            window,
            "previous_month",
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 3, 1),
            29);
    }

    [Fact]
    public void DuplicateKeysAreRejected()
    {
        var specs = new ReportWindowSpec[]
        {
            new("duplicate", ReportWindowKind.RollingDays, RollingDays: 7),
            new("duplicate", ReportWindowKind.RollingDays, RollingDays: 30),
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            ReportWindows.Validate(specs, allowCustomRange: true));

        Assert.Contains("duplicated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduledWindowsRejectCustomRanges()
    {
        var specs = new ReportWindowSpec[]
        {
            new(
                "custom",
                ReportWindowKind.CustomRange,
                CustomStartDate: new DateOnly(2026, 8, 1),
                CustomEndDate: new DateOnly(2026, 8, 25)),
        };

        Assert.Throws<ArgumentException>(() =>
            ReportWindows.Validate(specs, allowCustomRange: false));
    }

    private static void AssertWindow(
        ResolvedReportWindow window,
        string key,
        DateOnly startDate,
        DateOnly endDateExclusive,
        int dayCount)
    {
        Assert.Equal(key, window.Key);
        Assert.Equal(startDate, window.StartDate);
        Assert.Equal(endDateExclusive, window.EndDateExclusive);
        Assert.Equal(dayCount, window.DayCount);
    }
}
