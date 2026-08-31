using ClosedXML.Excel;
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
    public void CanonicalJsonMatchesGoldenFileAndXlsxWorkbookIsValid()
    {
        var report = CreateReport();

        var json = ReportCanonicalSerializer.Serialize(report);

        Assert.Equal(ReadGolden("report-v4.json").TrimEnd(), json);

        var xlsx = ReportXlsxSerializer.Serialize(report);
        using var workbook = ReportXlsxAssert.Open(xlsx);

        ReportXlsxAssert.AssertSheetOrder(
            workbook,
            ReportXlsxAssert.OverviewSheetName,
            ReportXlsxAssert.KeyDetailsSheetName,
            ReportXlsxAssert.UserSummarySheetName,
            ReportXlsxAssert.FailuresSheetName,
            ReportXlsxAssert.DataNotesSheetName);
        ReportXlsxAssert.AssertOverviewTitle(workbook);
        ReportXlsxAssert.AssertWorkbookIsPrintable(workbook);
        ReportXlsxAssert.AssertAllSheetsHaveTables(workbook);
        ReportXlsxAssert.AssertSheetFrozenHeader(workbook, ReportXlsxAssert.OverviewSheetName, headerRow: 14);
        foreach (var sheetName in new[]
                 {
                     ReportXlsxAssert.KeyDetailsSheetName,
                     ReportXlsxAssert.UserSummarySheetName,
                     ReportXlsxAssert.FailuresSheetName,
                     ReportXlsxAssert.DataNotesSheetName,
                 })
        {
            ReportXlsxAssert.AssertSheetFrozenHeader(workbook, sheetName, headerRow: 4);
        }

        var keyDetails = workbook.Worksheet(ReportXlsxAssert.KeyDetailsSheetName);
        Assert.Equal("API Key 用量明细", keyDetails.Cell(1, 1).GetString());
        Assert.Equal("Sub2API 用户", keyDetails.Cell(4, 1).GetString());
        Assert.Equal("总 Token 数（个）", keyDetails.Cell(4, 17).GetString());
        Assert.Equal(2, keyDetails.SheetView.SplitColumn);

        var userSummary = workbook.Worksheet(ReportXlsxAssert.UserSummarySheetName);
        Assert.Equal("Sub2API 用户用量汇总", userSummary.Cell(1, 1).GetString());

        var failures = workbook.Worksheet(ReportXlsxAssert.FailuresSheetName);
        Assert.Equal("采集异常明细", failures.Cell(1, 1).GetString());
        Assert.Equal("unavailable", failures.Cell(5, 8).GetString());

        var dataNotes = workbook.Worksheet(ReportXlsxAssert.DataNotesSheetName);
        Assert.Equal("数据与使用说明", dataNotes.Cell(1, 1).GetString());
        Assert.Equal("项目", dataNotes.Cell(4, 1).GetString());
        Assert.Equal("说明", dataNotes.Cell(4, 2).GetString());
    }

    [Fact]
    public void XlsxStoresPrecisionSensitiveNumbersAsPlainText()
    {
        var report = CreateReport();
        var xlsx = ReportXlsxSerializer.Serialize(report);

        using var workbook = ReportXlsxAssert.Open(xlsx);

        var overview = workbook.Worksheet(ReportXlsxAssert.OverviewSheetName);
        var overviewTokens = overview.Cell(16, 7);
        Assert.Equal(XLDataType.Text, overviewTokens.DataType);
        Assert.Equal("9007199254740993", overviewTokens.GetString());

        var keyDetails = workbook.Worksheet(ReportXlsxAssert.KeyDetailsSheetName);
        var keyId = keyDetails.Cell(5, 3);
        Assert.Equal(XLDataType.Text, keyId.DataType);
        Assert.Equal("9007199254740993", keyId.GetString());
        var keyTokens = keyDetails.Cell(6, 17);
        Assert.Equal(XLDataType.Text, keyTokens.DataType);
        Assert.Equal("9007199254740993", keyTokens.GetString());

        var failures = workbook.Worksheet(ReportXlsxAssert.FailuresSheetName);
        var failureUserId = failures.Cell(5, 1);
        Assert.Equal(XLDataType.Number, failureUserId.DataType);
        Assert.Equal(42, failureUserId.GetDouble());
        var failureUser = failures.Cell(5, 2);
        Assert.Equal(XLDataType.Text, failureUser.DataType);
        Assert.False(failureUser.HasFormula);
        Assert.Equal("=Synthetic User", failureUser.GetString());
    }

    [Fact]
    public void XlsxNeverStoresUntrustedStringsAsFormulas()
    {
        var report = CreateReport();
        using var workbook = ReportXlsxAssert.Open(ReportXlsxSerializer.Serialize(report));

        foreach (var worksheet in workbook.Worksheets)
        {
            foreach (var cell in worksheet.CellsUsed())
            {
                Assert.False(cell.HasFormula, $"{worksheet.Name}!{cell.Address} 不允许保存公式。");
            }
        }
    }

    [Fact]
    public void XlsxMarksFailedWindowsWithoutTouchingHealthyWindows()
    {
        var report = CreateReport();
        using var workbook = ReportXlsxAssert.Open(ReportXlsxSerializer.Serialize(report));

        var overview = workbook.Worksheet(ReportXlsxAssert.OverviewSheetName);
        Assert.Equal("部分采集失败", overview.Cell(16, 10).GetString());
        Assert.Equal("完整", overview.Cell(15, 10).GetString());

        var keyDetails = workbook.Worksheet(ReportXlsxAssert.KeyDetailsSheetName);
        Assert.Equal("完整", keyDetails.Cell(5, 20).GetString());
        Assert.Equal("采集失败", keyDetails.Cell(6, 20).GetString());

        var userSummary = workbook.Worksheet(ReportXlsxAssert.UserSummarySheetName);
        Assert.Equal("完整", userSummary.Cell(5, 18).GetString());
        Assert.Equal("部分采集失败", userSummary.Cell(6, 18).GetString());
    }

    [Fact]
    public void XlsxOmitsFailureSheetWhenReportIsComplete()
    {
        var report = CreateReport() with
        {
            Diagnostics = new ReportDiagnostics([]),
        };
        var xlsx = ReportXlsxSerializer.Serialize(report);

        using var workbook = ReportXlsxAssert.Open(xlsx);

        ReportXlsxAssert.AssertSheetOrder(
            workbook,
            ReportXlsxAssert.OverviewSheetName,
            ReportXlsxAssert.KeyDetailsSheetName,
            ReportXlsxAssert.UserSummarySheetName,
            ReportXlsxAssert.DataNotesSheetName);
        Assert.Equal("完整", workbook.Worksheet(ReportXlsxAssert.OverviewSheetName).Cell(4, 2).GetString());
    }

    [Fact]
    public void XlsxFileNameUsesLatestWindowDate()
    {
        var report = CreateReport();

        Assert.Equal("sub2api-report-2026-08-25.xlsx", ReportXlsxFileName.Create(report));
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
            "https://reports.example.com/api/v1/report-downloads/xlsx?token=synthetic",
            "1 天内有效，最多下载 20 次");
        var feishuLinkLines = ReportMessageRenderer.BuildFeishuLines(
            report,
            "https://reports.example.com/api/v1/report-downloads/xlsx?token=synthetic",
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
            line => line.Contains("[下载 XLSX 完整明细（1 天内有效，最多下载 20 次）]", StringComparison.Ordinal));
        Assert.Contains(
            feishuLinkLines,
            line => line.StartsWith("下载 XLSX 完整明细（1 天内有效，下载次数不限）：https://", StringComparison.Ordinal));
        var feishuLinkNode = Assert.Single(FeishuReportSender.CreatePostRow(
            feishuLinkLines.Single(line => line.StartsWith("下载 XLSX 完整明细", StringComparison.Ordinal))));
        Assert.Equal("a", feishuLinkNode.Tag);
        Assert.Equal("https://reports.example.com/api/v1/report-downloads/xlsx?token=synthetic", feishuLinkNode.Href);
        Assert.Contains("role=\"article\"", html, StringComparison.Ordinal);
        Assert.Contains(">最近 30 天</h2>", html, StringComparison.Ordinal);
        Assert.Contains(">Key 数（个）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">Token 数（个）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">实际费用（USD）</th>", html, StringComparison.Ordinal);
        Assert.Contains(">附件</strong>：XLSX 工作簿", html, StringComparison.Ordinal);
        Assert.Equal("sub2api-report-2026-08-25.xlsx", ReportXlsxFileName.Create(report));
        Assert.DoesNotContain("¥", html, StringComparison.Ordinal);
        Assert.DoesNotContain("$", string.Join('\n', dingTalkLines), StringComparison.Ordinal);
        Assert.DoesNotContain("$", string.Join('\n', feishuLines), StringComparison.Ordinal);
        Assert.DoesNotContain("$", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailRenderIncludesDatedXlsxAttachmentAndBinaryContent()
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

        Assert.Equal("sub2api-report-2026-08-25.xlsx", part.AttachmentFileName);
        Assert.NotNull(part.AttachmentContent);
        ReportXlsxAssert.AssertZipXlsxBytes(part.AttachmentContent);
        Assert.Equal(
            DeliveryPayloadHash.Compute(part.Subject, part.Body, part.AttachmentContent),
            part.PayloadHash);
        using var workbook = ReportXlsxAssert.Open(part.AttachmentContent);
        ReportXlsxAssert.AssertOverviewTitle(workbook);
        ReportXlsxAssert.AssertSheetOrder(
            workbook,
            ReportXlsxAssert.OverviewSheetName,
            ReportXlsxAssert.KeyDetailsSheetName,
            ReportXlsxAssert.UserSummarySheetName,
            ReportXlsxAssert.FailuresSheetName,
            ReportXlsxAssert.DataNotesSheetName);
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
