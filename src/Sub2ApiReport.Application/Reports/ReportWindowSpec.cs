using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sub2ApiReport.Application.Reports;

/// <summary>Defines how one report statistics window is derived from a cutoff date.</summary>
public enum ReportWindowKind
{
    /// <summary>A rolling window of complete natural days ending at the cutoff date.</summary>
    RollingDays,

    /// <summary>The last complete calendar week before the week containing the reference date.</summary>
    PreviousCalendarWeek,

    /// <summary>The last complete calendar month before the month containing the reference date.</summary>
    PreviousCalendarMonth,

    /// <summary>An explicitly bounded inclusive date range.</summary>
    CustomRange,
}

/// <summary>A named, stable specification of one report window submitted by callers.</summary>
public sealed record ReportWindowSpec(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays = null,
    DayOfWeek? WeekStartsOn = null,
    DateOnly? CustomStartDate = null,
    DateOnly? CustomEndDate = null);

/// <summary>A server-resolved window with an exclusive end date and a display label.</summary>
public sealed record ResolvedReportWindow(
    string Key,
    ReportWindowKind Kind,
    int? RollingDays,
    DayOfWeek? WeekStartsOn,
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    string Label)
{
    /// <summary>Gets the number of complete natural days covered by the window.</summary>
    public int DayCount => EndDateExclusive.DayNumber - StartDate.DayNumber;
}

/// <summary>Resolves and validates report window specifications against a cutoff date.</summary>
public static class ReportWindows
{
    /// <summary>Maximum number of windows in one report.</summary>
    public const int MaximumWindowCount = 8;

    /// <summary>Maximum length of a window key.</summary>
    public const int KeyMaxLength = 64;

    /// <summary>Maximum number of days for a rolling window.</summary>
    public const int MaximumRollingDays = 90;

    /// <summary>Maximum number of days for a custom range.</summary>
    public const int MaximumCustomRangeDays = 92;

    /// <summary>The canonical key of the default seven-day rolling window.</summary>
    public const string RollingSevenDaysKey = "rolling_7_days";

    /// <summary>The canonical key of the default thirty-day rolling window.</summary>
    public const string RollingThirtyDaysKey = "rolling_30_days";

    /// <summary>The default report windows used when no explicit specification is provided.</summary>
    public static IReadOnlyList<ReportWindowSpec> Default { get; } =
    [
        new(RollingSevenDaysKey, ReportWindowKind.RollingDays, RollingDays: 7),
        new(RollingThirtyDaysKey, ReportWindowKind.RollingDays, RollingDays: 30),
        new("previous_calendar_week", ReportWindowKind.PreviousCalendarWeek, WeekStartsOn: DayOfWeek.Monday),
        new("previous_calendar_month", ReportWindowKind.PreviousCalendarMonth),
    ];

    /// <summary>The legacy default windows kept for queued runs created before dynamic windows.</summary>
    public static IReadOnlyList<ReportWindowSpec> LegacyDefault { get; } =
    [
        new(RollingSevenDaysKey, ReportWindowKind.RollingDays, RollingDays: 7),
        new(RollingThirtyDaysKey, ReportWindowKind.RollingDays, RollingDays: 30),
    ];

    /// <summary>Validates a window specification list without resolving dates.</summary>
    public static void Validate(IReadOnlyList<ReportWindowSpec> specs, bool allowCustomRange)
    {
        ArgumentNullException.ThrowIfNull(specs);
        if (specs.Count is < 1 or > MaximumWindowCount)
        {
            throw new ArgumentException(
                $"A report requires between 1 and {MaximumWindowCount} windows.",
                nameof(specs));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            ValidateKey(spec.Key);
            if (!keys.Add(spec.Key))
            {
                throw new ArgumentException(
                    $"The report window key '{spec.Key}' is duplicated.",
                    nameof(specs));
            }

            switch (spec.Kind)
            {
                case ReportWindowKind.RollingDays:
                    if (spec.RollingDays is not (>= 1 and <= MaximumRollingDays))
                    {
                        throw new ArgumentException(
                            $"The rolling window '{spec.Key}' must cover 1 to {MaximumRollingDays} days.",
                            nameof(specs));
                    }

                    break;
                case ReportWindowKind.PreviousCalendarWeek:
                    break;
                case ReportWindowKind.PreviousCalendarMonth:
                    break;
                case ReportWindowKind.CustomRange:
                    if (!allowCustomRange)
                    {
                        throw new ArgumentException(
                            $"The scheduled report window '{spec.Key}' cannot use a custom range.",
                            nameof(specs));
                    }

                    if (spec.CustomStartDate is null || spec.CustomEndDate is null)
                    {
                        throw new ArgumentException(
                            $"The custom window '{spec.Key}' requires both a start and an end date.",
                            nameof(specs));
                    }

                    break;
                default:
                    throw new ArgumentException(
                        "The report window kind is invalid.",
                        nameof(specs));
            }
        }
    }

    /// <summary>Resolves validated specifications into concrete windows for the cutoff date.</summary>
    public static IReadOnlyList<ResolvedReportWindow> Resolve(
        IReadOnlyList<ReportWindowSpec> specs,
        DateOnly cutoffDate,
        bool allowCustomRange)
    {
        Validate(specs, allowCustomRange);
        var referenceDate = cutoffDate.AddDays(1);
        var resolved = new List<ResolvedReportWindow>(specs.Count);
        foreach (var spec in specs)
        {
            resolved.Add(spec.Kind switch
            {
                ReportWindowKind.RollingDays => new ResolvedReportWindow(
                    spec.Key,
                    spec.Kind,
                    spec.RollingDays,
                    null,
                    cutoffDate.AddDays(-spec.RollingDays!.Value + 1),
                    referenceDate,
                    CreateRollingLabel(spec.RollingDays.Value)),
                ReportWindowKind.PreviousCalendarWeek => ResolvePreviousWeek(spec, referenceDate),
                ReportWindowKind.PreviousCalendarMonth => ResolvePreviousMonth(spec, referenceDate),
                ReportWindowKind.CustomRange => ResolveCustomRange(spec, cutoffDate),
                _ => throw new ArgumentException("The report window kind is invalid.", nameof(specs)),
            });
        }

        return resolved;
    }

    private static ResolvedReportWindow ResolvePreviousWeek(ReportWindowSpec spec, DateOnly referenceDate)
    {
        var weekStartsOn = spec.WeekStartsOn ?? DayOfWeek.Monday;
        var currentWeekStart = referenceDate.AddDays(
            -((int)referenceDate.DayOfWeek - (int)weekStartsOn + 7) % 7);
        var start = currentWeekStart.AddDays(-7);
        return new ResolvedReportWindow(
            spec.Key,
            spec.Kind,
            null,
            weekStartsOn,
            start,
            currentWeekStart,
            "上一完整自然周");
    }

    private static ResolvedReportWindow ResolvePreviousMonth(ReportWindowSpec spec, DateOnly referenceDate)
    {
        var currentMonthStart = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
        var start = currentMonthStart.AddMonths(-1);
        return new ResolvedReportWindow(
            spec.Key,
            spec.Kind,
            null,
            null,
            start,
            currentMonthStart,
            "上一完整自然月");
    }

    private static ResolvedReportWindow ResolveCustomRange(ReportWindowSpec spec, DateOnly cutoffDate)
    {
        var inclusiveEnd = spec.CustomEndDate!.Value;
        if (spec.CustomStartDate!.Value > inclusiveEnd)
        {
            throw new ArgumentException(
                $"The custom window '{spec.Key}' must start on or before its end date.",
                nameof(spec));
        }

        if (inclusiveEnd > cutoffDate)
        {
            throw new ReportGenerationPreconditionException(
                $"The custom window '{spec.Key}' cannot end after the report cutoff date.");
        }

        var endExclusive = inclusiveEnd.AddDays(1);
        if (endExclusive.DayNumber - spec.CustomStartDate.Value.DayNumber > MaximumCustomRangeDays)
        {
            throw new ArgumentException(
                $"The custom window '{spec.Key}' cannot span more than {MaximumCustomRangeDays} days.",
                nameof(spec));
        }

        return new ResolvedReportWindow(
            spec.Key,
            spec.Kind,
            null,
            null,
            spec.CustomStartDate.Value,
            endExclusive,
            "自定义区间");
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > KeyMaxLength)
        {
            throw new ArgumentException(
                $"The report window key cannot exceed {KeyMaxLength} characters.",
                nameof(key));
        }

        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                throw new ArgumentException(
                    "The report window key may only contain ASCII letters, digits, hyphens, and underscores.",
                    nameof(key));
            }
        }
    }

    private static string CreateRollingLabel(int days) => string.Create(
        CultureInfo.InvariantCulture,
        $"最近 {days} 天");
}

/// <summary>Serializes frozen window specifications and resolved windows for queued runs.</summary>
public static class ReportWindowJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a window specification list for persistence.</summary>
    public static string SerializeSpecs(IReadOnlyList<ReportWindowSpec> specs) =>
        JsonSerializer.Serialize(specs, Options);

    /// <summary>Deserializes a persisted window specification list.</summary>
    public static IReadOnlyList<ReportWindowSpec> DeserializeSpecs(string json) =>
        JsonSerializer.Deserialize<ReportWindowSpec[]>(json, Options)
        ?? throw new InvalidOperationException("The stored report window specification is invalid.");

    /// <summary>Serializes resolved windows for persistence.</summary>
    public static string SerializeResolved(IReadOnlyList<ResolvedReportWindow> windows) =>
        JsonSerializer.Serialize(windows, Options);

    /// <summary>Deserializes persisted resolved windows.</summary>
    public static IReadOnlyList<ResolvedReportWindow> DeserializeResolved(string json) =>
        JsonSerializer.Deserialize<ResolvedReportWindow[]>(json, Options)
        ?? throw new InvalidOperationException("The stored report window resolution is invalid.");
}
