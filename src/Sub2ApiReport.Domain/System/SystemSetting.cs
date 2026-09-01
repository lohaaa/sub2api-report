namespace Sub2ApiReport.Domain.System;

public sealed class SystemSetting
{
    public const int SingletonId = 1;
    public const string DefaultTimezone = "Asia/Shanghai";
    public const string DefaultLogLevel = "Information";
    public const int DefaultReportConcurrency = 4;
    public const int DefaultReportRetentionMonths = 12;
    public const int DefaultBackupRetentionCount = 10;
    public const int DefaultReportDownloadLinkHours = 24;
    public const int MaximumReportDownloadLinkHours = 24 * 30;
    public const int MaximumReportDownloadCount = 10_000;

    private static readonly string[] AllowedLogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    private SystemSetting()
    {
    }

    public int Id { get; private init; } = SingletonId;

    public DateTimeOffset? InitializedAt { get; private set; }

    public string Timezone { get; private set; } = DefaultTimezone;


    public string LogLevel { get; private set; } = DefaultLogLevel;

    public int ReportConcurrency { get; private set; } = DefaultReportConcurrency;

    public int ReportRetentionMonths { get; private set; } = DefaultReportRetentionMonths;

    public int BackupRetentionCount { get; private set; } = DefaultBackupRetentionCount;

    public string? ReportExternalBaseUrl { get; private set; }

    public int ReportDownloadLinkHours { get; private set; } = DefaultReportDownloadLinkHours;

    public int? ReportDownloadMaxDownloads { get; private set; }

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
        string logLevel,
        int reportConcurrency,
        int reportRetentionMonths,
        int backupRetentionCount,
        string? reportExternalBaseUrl,
        int reportDownloadLinkHours,
        int? reportDownloadMaxDownloads,
        DateTimeOffset updatedAt)
    {
        var validatedTimezone = ValidateText(timezone, 100, nameof(timezone));
        var validatedLogLevel = AllowedLogLevels.FirstOrDefault(level =>
            level.Equals(logLevel, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unsupported log level.", nameof(logLevel));

        if (reportConcurrency is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportConcurrency),
                "Report concurrency must be between 1 and 10.");
        }

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

        if (reportDownloadLinkHours is < 1 or > MaximumReportDownloadLinkHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportDownloadLinkHours),
                $"Report download link lifetime must be between 1 and {MaximumReportDownloadLinkHours} hours.");
        }

        if (reportDownloadMaxDownloads is < 1 or > MaximumReportDownloadCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportDownloadMaxDownloads),
                $"Report download count must be between 1 and {MaximumReportDownloadCount}, or unlimited.");
        }

        var validatedExternalBaseUrl = ValidateExternalBaseUrl(
            reportExternalBaseUrl,
            nameof(reportExternalBaseUrl));

        Timezone = validatedTimezone;
        LogLevel = validatedLogLevel;
        ReportConcurrency = reportConcurrency;
        ReportRetentionMonths = reportRetentionMonths;
        BackupRetentionCount = backupRetentionCount;
        ReportExternalBaseUrl = validatedExternalBaseUrl;
        ReportDownloadLinkHours = reportDownloadLinkHours;
        ReportDownloadMaxDownloads = reportDownloadMaxDownloads;
        UpdatedAt = updatedAt;
        Revision++;
    }

    private static string? ValidateExternalBaseUrl(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().TrimEnd('/');
        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "The report external base URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.",
                parameterName);
        }

        return normalized;
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
