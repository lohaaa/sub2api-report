using ClosedXML.Excel;

namespace Sub2ApiReport.IntegrationTests;

/// <summary>
/// Shared assertions for generated XLSX workbooks. All workbook scenarios in
/// these tests use synthetic data and reserved example domains only.
/// </summary>
internal static class ReportXlsxAssert
{
    public const string XlsxMediaType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string OverviewSheetName = "报告概览";
    public const string KeyDetailsSheetName = "Key 明细";
    public const string UserSummarySheetName = "用户汇总";
    public const string FailuresSheetName = "采集异常";
    public const string DataNotesSheetName = "数据说明";

    public static readonly byte[] ZipLocalFileHeaderBytes = [0x50, 0x4B, 0x03, 0x04];

    public static void AssertZipXlsxBytes(ReadOnlySpan<byte> content)
    {
        Assert.False(content.IsEmpty);
        Assert.True(
            content.StartsWith(ZipLocalFileHeaderBytes),
            "XLSX 内容必须以 ZIP 本地文件头（PK\\x03\\x04）开始。");
    }

    public static XLWorkbook Open(byte[] content)
    {
        AssertZipXlsxBytes(content);
        return new XLWorkbook(new MemoryStream(content));
    }

    public static void AssertSheetOrder(XLWorkbook workbook, params string[] expectedNames) =>
        Assert.Equal(expectedNames, workbook.Worksheets.Select(worksheet => worksheet.Name).ToArray());

    public static void AssertOverviewTitle(XLWorkbook workbook)
    {
        var overview = workbook.Worksheet(OverviewSheetName);
        Assert.Equal("Sub2API Codex 用量报告", overview.Cell(1, 1).GetString());
        Assert.Equal(
            "基于不可变快照生成；统计窗口均为完整自然日，费用币种为 USD。",
            overview.Cell(2, 1).GetString());
    }

    public static void AssertSheetFrozenHeader(XLWorkbook workbook, string sheetName, int headerRow)
    {
        var worksheet = workbook.Worksheet(sheetName);
        Assert.True(
            worksheet.SheetView.SplitRow >= headerRow,
            $"{sheetName} 必须冻结表头行。");
    }

    public static void AssertWorkbookIsPrintable(XLWorkbook workbook)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            Assert.NotEqual(XLPageOrientation.Default, worksheet.PageSetup.PageOrientation);
            Assert.Equal(1, worksheet.PageSetup.PagesWide);
        }
    }

    public static void AssertAllSheetsHaveTables(XLWorkbook workbook)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            Assert.NotEmpty(worksheet.Tables.ToArray());
            foreach (var table in worksheet.Tables)
            {
                Assert.True(table.ShowAutoFilter, $"{worksheet.Name} 表格必须启用筛选。");
            }
        }
    }
}
