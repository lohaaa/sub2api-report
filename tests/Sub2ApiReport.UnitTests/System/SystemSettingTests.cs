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
        Assert.Equal("stable", setting.ReleaseChannel);
        Assert.Equal("Information", setting.LogLevel);
        Assert.Equal(12, setting.ReportRetentionMonths);
        Assert.Equal(10, setting.BackupRetentionCount);
        Assert.Equal(1, setting.Revision);
        Assert.Null(setting.InitializedAt);
        Assert.Null(setting.UpdatedAt);
    }

    [Fact]
    public void UpdateChangesMutableSettingsAndRevision()
    {
        var setting = SystemSetting.CreateDefault();
        var updatedAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

        setting.Update("UTC", "preview", "Warning", 24, 20, updatedAt);

        Assert.Equal("UTC", setting.Timezone);
        Assert.Equal("preview", setting.ReleaseChannel);
        Assert.Equal("Warning", setting.LogLevel);
        Assert.Equal(24, setting.ReportRetentionMonths);
        Assert.Equal(20, setting.BackupRetentionCount);
        Assert.Equal(2, setting.Revision);
        Assert.Equal(updatedAt, setting.UpdatedAt);
    }

    [Fact]
    public void UpdateRejectsUnsupportedLogLevel()
    {
        var setting = SystemSetting.CreateDefault();

        var exception = Assert.Throws<ArgumentException>(() =>
            setting.Update("UTC", "stable", "Everything", 12, 10, DateTimeOffset.UtcNow));

        Assert.Equal("logLevel", exception.ParamName);
        Assert.Equal(1, setting.Revision);
    }
}
