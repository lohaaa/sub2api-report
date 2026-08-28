namespace Sub2ApiReport.Domain.Reports;

public sealed class ReportSchedule
{
    public const int SingletonId = 1;
    public const int DefaultDayOfMonth = 1;
    public const string DefaultLocalTime = "09:00";
    public const string DefaultTimezone = "Asia/Shanghai";

    private ReportSchedule()
    {
    }

    public int Id { get; private init; } = SingletonId;

    public bool Enabled { get; private set; }

    public int DayOfMonth { get; private set; } = DefaultDayOfMonth;

    public string LocalTime { get; private set; } = DefaultLocalTime;

    public string Timezone { get; private set; } = DefaultTimezone;

    public long Revision { get; private set; } = 1;

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Gets the serialized window specification list; null means the default windows.</summary>
    public string? WindowSpecsJson { get; private set; }

    public static ReportSchedule CreateDefault() => new();

    public void Update(
        bool enabled,
        int dayOfMonth,
        string localTime,
        string timezone,
        string? windowSpecsJson,
        DateTimeOffset updatedAt)
    {
        if (dayOfMonth is < 1 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfMonth),
                "The report day must be between 1 and 28.");
        }

        var parsedTime = ParseLocalTime(localTime);
        var normalizedTimezone = ValidateText(timezone, 100, nameof(timezone));

        Enabled = enabled;
        DayOfMonth = dayOfMonth;
        LocalTime = parsedTime.ToString("HH:mm", global::System.Globalization.CultureInfo.InvariantCulture);
        Timezone = normalizedTimezone;
        WindowSpecsJson = windowSpecsJson;
        UpdatedAt = updatedAt;
        Revision++;
    }

    public static TimeOnly ParseLocalTime(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return TimeOnly.TryParseExact(
            value.Trim(),
            "HH:mm",
            global::System.Globalization.CultureInfo.InvariantCulture,
            global::System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : throw new ArgumentException("The local time must use HH:mm format.", nameof(value));
    }

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
