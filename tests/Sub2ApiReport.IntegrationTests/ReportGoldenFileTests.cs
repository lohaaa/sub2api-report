using System.Text;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Notifications;
using Sub2ApiReport.Infrastructure.Reports;

namespace Sub2ApiReport.IntegrationTests;

public sealed class ReportGoldenFileTests
{
    [Fact]
    public void CanonicalJsonAndCsvMatchGoldenFiles()
    {
        var report = CreateReport();

        var json = ReportCanonicalSerializer.Serialize(report);
        var csv = ReportCsvSerializer.Serialize(report);

        Assert.True(csv.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var csvText = Encoding.UTF8.GetString(csv[Encoding.UTF8.GetPreamble().Length..])
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(ReadGolden("report-v4.json").TrimEnd(), json);
        Assert.Equal(ReadGolden("report-v4.csv").Replace("\r\n", "\n", StringComparison.Ordinal), csvText);
    }

    [Fact]
    public void SchemaThreeSnapshotMapsIntoDynamicWindows()
    {
        var report = ReportCanonicalSerializer.Deserialize(ReadGolden("report-v3.json"), 3);

        Assert.Equal(3, report.SchemaVersion);
        Assert.Collection(
            report.Windows,
            window => Assert.Equal(ReportWindows.RollingSevenDaysKey, window.Key),
            window => Assert.Equal(ReportWindows.RollingThirtyDaysKey, window.Key));
        Assert.All(report.Users, user => Assert.Equal(2, user.Windows.Count));
        Assert.All(report.Keys, key => Assert.Equal(2, key.Windows.Count));
    }

    [Fact]
    public void NotificationBodiesIncludeMeasurementAndCurrencyUnits()
    {
        var report = CreateReport();

        var dingTalkLines = ReportMessageRenderer.BuildDingTalkLines(report);
        var feishuLines = ReportMessageRenderer.BuildFeishuLines(report);
        var dingTalkLinkLines = ReportMessageRenderer.BuildDingTalkLines(
            report,
            "https://reports.example.com/api/v1/report-downloads/csv?token=synthetic",
            "1 天内有效，最多下载 20 次");
        var feishuLinkLines = ReportMessageRenderer.BuildFeishuLines(
            report,
            "https://reports.example.com/api/v1/report-downloads/csv?token=synthetic",
            "1 天内有效，下载次数不限");
        var html = ReportMessageRenderer.BuildHtmlBody(report);

        Assert.Contains("### Codex 用量摘要", dingTalkLines);
        Assert.Contains(dingTalkLines, line => line.Contains("Token 数（个）：**9,007,199,254,740,993**", StringComparison.Ordinal));
        Assert.Contains(dingTalkLines, line => line.Contains("实际费用（USD）：**3.25**", StringComparison.Ordinal));
        Assert.Contains(feishuLines, line => line.Contains("【最近 30 天】", StringComparison.Ordinal));
        Assert.Contains(feishuLines, line => line.Contains("Token 数（个） 9,007,199,254,740,993", StringComparison.Ordinal));
        Assert.Contains(feishuLines, line => line.Contains("实际费用（USD） 3.25", StringComparison.Ordinal));
        Assert.Contains(
            dingTalkLinkLines,
            line => line.Contains("[下载 CSV 完整明细（1 天内有效，最多下载 20 次）]", StringComparison.Ordinal));
        Assert.Contains(
            feishuLinkLines,
            line => line.StartsWith("下载 CSV 完整明细（1 天内有效，下载次数不限）：https://", StringComparison.Ordinal));
        var feishuLinkNode = Assert.Single(FeishuReportSender.CreatePostRow(
            feishuLinkLines.Single(line => line.StartsWith("下载 CSV 完整明细", StringComparison.Ordinal))));
        Assert.Equal("a", feishuLinkNode.Tag);
        Assert.Equal("https://reports.example.com/api/v1/report-downloads/csv?token=synthetic", feishuLinkNode.Href);
        Assert.Contains("role=\"article\"", html, StringComparison.Ordinal);
        Assert.Contains(">最近 30 天</h2>", html, StringComparison.Ordinal);
        Assert.Contains(">Key 数（个）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">Token 数（个）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">实际费用（USD）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">附件</strong>：CSV 文件", html, StringComparison.Ordinal);
        Assert.Equal("sub2api-report-2026-08-25.csv", ReportCsvFileName.Create(report));
        Assert.DoesNotContain("¥", html, StringComparison.Ordinal);
        Assert.DoesNotContain("$", string.Join('\n', dingTalkLines), StringComparison.Ordinal);
        Assert.DoesNotContain("$", string.Join('\n', feishuLines), StringComparison.Ordinal);
        Assert.DoesNotContain("$", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailRenderIncludesDatedCsvAttachmentMetadata()
    {
        var report = CreateReport();
        var sender = new EmailReportSender(TimeProvider.System);
        var context = ChannelDeliveryContext.ForEmail(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "合成邮件渠道",
            new EmailDeliveryOptions(
                "smtp.example.com",
                587,
                SmtpSecurityMode.StartTls,
                null,
                null,
                "reports@example.com",
                "Sub2API Report",
                ["recipient@example.com"],
                []));

        var part = Assert.Single(sender.Render(report, context));

        Assert.Equal("sub2api-report-2026-08-25.csv", part.CsvFileName);
        Assert.NotNull(part.CsvContent);
        Assert.StartsWith("\uFEFF", part.CsvContent, StringComparison.Ordinal);
    }


    private static ReportDocument CreateReport()
    {
        var sevenDay = Metrics(7, 700, 1.25m, 0.75m);
        var thirtyDay = Metrics(30, 9007199254740993, 4.5m, 3.25m);
        var sevenWindow = new ReportWindowDescriptor(
            ReportWindows.RollingSevenDaysKey,
            ReportWindowKind.RollingDays,
            7,
            null,
            new DateOnly(2026, 8, 19),
            new DateOnly(2026, 8, 26),
            7,
            "最近 7 天");
        var thirtyWindow = new ReportWindowDescriptor(
            ReportWindows.RollingThirtyDaysKey,
            ReportWindowKind.RollingDays,
            30,
            null,
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 8, 26),
            30,
            "最近 30 天");
        ReportWindowMetrics[] windowMetrics =
        [
            new(ReportWindows.RollingSevenDaysKey, sevenDay),
            new(ReportWindows.RollingThirtyDaysKey, thirtyDay),
        ];
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var keyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        return new ReportDocument(
            ReportSnapshot.CurrentSchemaVersion,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReportStatus.Complete,
            ReportTrigger.ManualDryRun,
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "Asia/Shanghai",
            3,
            [sevenWindow, thirtyWindow],
            windowMetrics,
            [new ReportUserUsage(userId, 42, "synthetic-user", "=Synthetic User", 1, windowMetrics)],
            [new ReportKeyUsage(
                keyId,
                "9007199254740993",
                42,
                "=Synthetic User",
                "Synthetic Key",
                "active",
                null,
                null,
                windowMetrics)],
            new ReportDiagnostics([new ReportRangeFailure(
                42,
                "=Synthetic User",
                9007199254740993,
                "Synthetic Key",
                ReportWindows.RollingThirtyDaysKey,
                new DateOnly(2026, 7, 27),
                new DateOnly(2026, 8, 26),
                Sub2ApiFailureKind.Unavailable,
                "unavailable")]));
    }

    private static ReportUsageMetrics Metrics(
        long requests,
        long tokens,
        decimal cost,
        decimal actualCost) => new(
            requests,
            tokens - 50,
            25,
            25,
            10,
            15,
            tokens,
            cost,
            actualCost,
            125.5m);

    private static string ReadGolden(string fileName) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", fileName));
}
