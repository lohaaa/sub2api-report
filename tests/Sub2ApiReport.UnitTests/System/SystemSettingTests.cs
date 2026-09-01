using Sub2ApiReport.Domain.System;

namespace Sub2ApiReport.UnitTests.System;

public sealed class SystemSettingTests
{
    [Fact]
    public void CreateDefaultUsesDocumentedOperationalDefaults()
    {
        var setting = SystemSetting.CreateDefault();

        Assert.Equal(SystemSetting.SingletonId, setting.Id);
        Assert.Equal("Asia/Shanghai", setting.Timezone);
        Assert.Equal("Information", setting.LogLevel);
        Assert.Equal(4, setting.ReportConcurrency);
        Assert.Equal(12, setting.ReportRetentionMonths);
        Assert.Equal(10, setting.BackupRetentionCount);
        Assert.Null(setting.ReportExternalBaseUrl);
        Assert.Equal(24, setting.ReportDownloadLinkHours);
        Assert.Null(setting.ReportDownloadMaxDownloads);
        Assert.Equal(1, setting.Revision);
        Assert.Null(setting.InitializedAt);
        Assert.Null(setting.UpdatedAt);
    }

    [Fact]
    public void UpdateChangesMutableSettingsAndRevision()
    {
        var setting = SystemSetting.CreateDefault();
        var updatedAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

        setting.Update(
            "UTC",
            "Warning",
            6,
            24,
            20,
            "http://127.0.0.1:5173",
            48,
            20,
            updatedAt);

        Assert.Equal("UTC", setting.Timezone);
        Assert.Equal("Warning", setting.LogLevel);
        Assert.Equal(6, setting.ReportConcurrency);
        Assert.Equal(24, setting.ReportRetentionMonths);
        Assert.Equal(20, setting.BackupRetentionCount);
        Assert.Equal("http://127.0.0.1:5173", setting.ReportExternalBaseUrl);
        Assert.Equal(48, setting.ReportDownloadLinkHours);
        Assert.Equal(20, setting.ReportDownloadMaxDownloads);
        Assert.Equal(2, setting.Revision);
        Assert.Equal(updatedAt, setting.UpdatedAt);
    }

    [Fact]
    public void UpdateRejectsUnsupportedLogLevel()
    {
        var setting = SystemSetting.CreateDefault();

        var exception = Assert.Throws<ArgumentException>(() =>
            setting.Update(
                "UTC",
                "Everything",
                4,
                12,
                10,
                null,
                24,
                null,
                DateTimeOffset.UtcNow));

        Assert.Equal("logLevel", exception.ParamName);
        Assert.Equal(1, setting.Revision);
    }

    [Theory]
    [InlineData("ftp://reports.example.com")]
    [InlineData("https://user:password@reports.example.com")]
    [InlineData("https://reports.example.com?token=secret")]
    public void UpdateRejectsUnsafeReportExternalBaseUrl(string externalBaseUrl)
    {
        var setting = SystemSetting.CreateDefault();

        var exception = Assert.Throws<ArgumentException>(() =>
            setting.Update(
                "UTC",
                "Information",
                4,
                12,
                10,
                externalBaseUrl,
                24,
                null,
                DateTimeOffset.UtcNow));

        Assert.Equal("reportExternalBaseUrl", exception.ParamName);
        Assert.Equal(1, setting.Revision);
    }
}
