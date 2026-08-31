namespace Sub2ApiReport.Domain.Reports;

/// <summary>
/// Describes how the monthly report behaves in months that are shorter than the
/// configured day of month.
/// </summary>
public enum ShortMonthStrategy
{
    /// <summary>Runs on the last day of shorter months (for example day 31 runs on Feb 28/29).</summary>
    UseLastDay = 0,

    /// <summary>Skips execution entirely in months that have no matching day.</summary>
    SkipMonth = 1,
}
