namespace Sub2ApiReport.Domain.System;

public sealed class SystemSetting
{
    public const int SingletonId = 1;
    public const string DefaultTimezone = "Asia/Shanghai";
    public const string DefaultReleaseChannel = "stable";
    public const string DefaultLogLevel = "Information";
    public const int DefaultReportRetentionMonths = 12;
    public const int DefaultBackupRetentionCount = 10;

    private static readonly string[] AllowedLogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    private SystemSetting()
    {
    }

    public int Id { get; private init; } = SingletonId;

    public DateTimeOffset? InitializedAt { get; private set; }

    public string Timezone { get; private set; } = DefaultTimezone;

    public string ReleaseChannel { get; private set; } = DefaultReleaseChannel;

    public string LogLevel { get; private set; } = DefaultLogLevel;

    public int ReportRetentionMonths { get; private set; } = DefaultReportRetentionMonths;

    public int BackupRetentionCount { get; private set; } = DefaultBackupRetentionCount;

    public long Revision { get; private set; } = 1;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static SystemSetting CreateDefault() => new();

    public void MarkInitialized(DateTimeOffset initializedAt)
    {
        if (InitializedAt is not null)
        {
            throw new InvalidOperationException("The system is already initialized.");
        }

        InitializedAt = initializedAt;
        UpdatedAt = initializedAt;
        Revision++;
    }

    public void Update(
        string timezone,
        string releaseChannel,
        string logLevel,
        int reportRetentionMonths,
        int backupRetentionCount,
        DateTimeOffset updatedAt)
    {
        var validatedTimezone = ValidateText(timezone, 100, nameof(timezone));
        var validatedReleaseChannel = ValidateText(releaseChannel, 32, nameof(releaseChannel));
        var validatedLogLevel = AllowedLogLevels.FirstOrDefault(level =>
            level.Equals(logLevel, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unsupported log level.", nameof(logLevel));

        if (reportRetentionMonths is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportRetentionMonths),
                "Report retention must be between 1 and 120 months.");
        }

        if (backupRetentionCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backupRetentionCount),
                "Backup retention must be between 1 and 100.");
        }

        Timezone = validatedTimezone;
        ReleaseChannel = validatedReleaseChannel;
        LogLevel = validatedLogLevel;
        ReportRetentionMonths = reportRetentionMonths;
        BackupRetentionCount = backupRetentionCount;
        UpdatedAt = updatedAt;
        Revision++;
    }

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
