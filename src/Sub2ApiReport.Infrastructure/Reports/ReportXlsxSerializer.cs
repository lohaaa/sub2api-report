using System.Globalization;
using ClosedXML.Excel;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Reports;

internal static class ReportXlsxSerializer
{
    private const long MaximumExactExcelInteger = 999_999_999_999_999;
    private const string HeaderColor = "183B4E";
    private const string AccentColor = "2A7F62";
    private const string WarningColor = "B5473A";
    private const string MutedBackgroundColor = "EEF2F4";
    private const string TextColor = "172B35";
    private const string MutedTextColor = "526772";
    private const string DateFormat = "yyyy-mm-dd";
    private const string IntegerFormat = "#,##0";
    private const string CostFormat = "#,##0.00######";

    public static byte[] Serialize(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Sub2API Codex 用量报告";
        workbook.Properties.Subject = "Sub2API 用户、API Key 与统计窗口用量";
        workbook.Properties.Author = "Sub2API Report";
        workbook.Properties.Company = "Sub2API Report";
        workbook.Properties.Comments = "从不可变 canonical snapshot 生成。";

        AddOverviewSheet(workbook, report);
        AddKeyDetailsSheet(workbook, report);
        AddUserSummarySheet(workbook, report);
        if (report.Diagnostics.FailedRanges.Count > 0)
        {
            AddFailuresSheet(workbook, report);
        }

        AddDataNotesSheet(workbook);

        using var output = new MemoryStream();
        workbook.SaveAs(output, validate: true, evaluateFormulae: false);
        return output.ToArray();
    }

    private static void AddOverviewSheet(XLWorkbook workbook, ReportDocument report)
    {
        var worksheet = workbook.Worksheets.Add("报告概览");
        ConfigureWorksheet(worksheet, XLColor.FromHtml(AccentColor));
        AddTitle(
            worksheet,
            "Sub2API Codex 用量报告",
            "基于不可变快照生成；统计窗口均为完整自然日，费用币种为 USD。");

        var metadata = new (string Label, object Value)[]
        {
            ("报告状态", GetStatusLabel(report.Status)),
            ("报告编号", report.ReportId.ToString("D")),
            ("生成时间", report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)),
            ("统计时区", report.Timezone),
            ("Sub2API 用户数", report.Users.Count),
            ("API Key 数", report.Keys.Count),
            ("失败区间数", report.Diagnostics.FailedRanges.Count),
            ("连接配置版本", report.ConnectionRevision),
        };

        for (var index = 0; index < metadata.Length; index++)
        {
            var row = 4 + index;
            var labelCell = worksheet.Cell(row, 1);
            SetText(labelCell, metadata[index].Label);
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
            labelCell.Style.Fill.BackgroundColor = XLColor.FromHtml(MutedBackgroundColor);
            labelCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            labelCell.Style.Border.BottomBorderColor = XLColor.White;

            var valueCell = worksheet.Cell(row, 2);
            SetCellValue(valueCell, metadata[index].Value);
            valueCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            valueCell.Style.Border.BottomBorderColor = XLColor.FromHtml(MutedBackgroundColor);
        }

        var statusCell = worksheet.Cell(4, 2);
        statusCell.Style.Font.Bold = true;
        statusCell.Style.Font.FontColor = XLColor.White;
        statusCell.Style.Fill.BackgroundColor = report.Status == ReportStatus.Complete
            ? XLColor.FromHtml(AccentColor)
            : XLColor.FromHtml(WarningColor);

        const int tableHeaderRow = 14;
        SetText(worksheet.Cell(tableHeaderRow - 1, 1), "统计窗口总览");
        StyleSectionHeading(worksheet.Cell(tableHeaderRow - 1, 1));

        string[] headers =
        [
            "窗口名称",
            "窗口类型",
            "开始日期",
            "结束日期",
            "天数",
            "请求数（次）",
            "总 Token 数（个）",
            "实际费用（USD）",
            "日均实际费用（USD/日）",
            "数据状态",
        ];
        WriteHeaders(worksheet, tableHeaderRow, headers);

        var totals = report.WindowTotals.ToDictionary(item => item.WindowKey, item => item.Metrics);
        for (var index = 0; index < report.Windows.Count; index++)
        {
            var descriptor = report.Windows[index];
            totals.TryGetValue(descriptor.Key, out var metrics);
            var row = tableHeaderRow + 1 + index;
            SetText(worksheet.Cell(row, 1), ReportWindows.GetDisplayLabel(descriptor.Kind, descriptor.Label));
            SetText(worksheet.Cell(row, 2), GetWindowKindLabel(descriptor.Kind));
            SetDate(worksheet.Cell(row, 3), descriptor.StartDate);
            SetDate(worksheet.Cell(row, 4), descriptor.EndDateExclusive.AddDays(-1));
            SetInteger(worksheet.Cell(row, 5), descriptor.DayCount);
            SetInteger(worksheet.Cell(row, 6), metrics?.TotalRequests ?? 0);
            SetInteger(worksheet.Cell(row, 7), metrics?.TotalTokens ?? 0);
            SetCost(worksheet.Cell(row, 8), metrics?.TotalActualCost ?? 0m);
            SetCost(
                worksheet.Cell(row, 9),
                (metrics?.TotalActualCost ?? 0m) / Math.Max(descriptor.DayCount, 1));
            SetText(worksheet.Cell(row, 10), HasWindowFailure(report, descriptor.Key) ? "部分采集失败" : "完整");
        }

        var lastRow = Math.Max(tableHeaderRow, tableHeaderRow + report.Windows.Count);
        CreateTable(worksheet, tableHeaderRow, lastRow, headers.Length, "WindowSummary");
        worksheet.SheetView.FreezeRows(tableHeaderRow);
        worksheet.Columns(1, 10).Width = 16;
        worksheet.Column(1).Width = 24;
        worksheet.Column(2).Width = 18;
        worksheet.Columns(3, 4).Width = 13;
        worksheet.Columns(6, 7).Width = 21;
        worksheet.Columns(8, 9).Width = 24;
        worksheet.Column(10).Width = 18;
        ConfigurePrint(worksheet, tableHeaderRow, landscape: true);
    }

    private static void AddKeyDetailsSheet(XLWorkbook workbook, ReportDocument report)
    {
        var worksheet = workbook.Worksheets.Add("Key 明细");
        ConfigureWorksheet(worksheet, XLColor.FromHtml(HeaderColor));
        AddTitle(
            worksheet,
            "API Key 用量明细",
            "每行表示一个 API Key 在一个统计窗口内的用量；未提供的生命周期时间显示为“不适用”。");

        const int tableHeaderRow = 4;
        string[] headers =
        [
            "Sub2API 用户",
            "Key 名称",
            "Key ID",
            "Key 状态",
            "最后使用时间",
            "退役时间",
            "窗口名称",
            "窗口类型",
            "开始日期",
            "结束日期",
            "天数",
            "请求数（次）",
            "输入 Token 数（个）",
            "输出 Token 数（个）",
            "缓存创建 Token 数（个）",
            "缓存读取 Token 数（个）",
            "总 Token 数（个）",
            "实际费用（USD）",
            "日均实际费用（USD/日）",
            "数据状态",
        ];
        WriteHeaders(worksheet, tableHeaderRow, headers);

        var row = tableHeaderRow + 1;
        foreach (var key in report.Keys)
        {
            foreach (var window in OrderByWindows(report, key.Windows))
            {
                var descriptor = FindWindow(report, window.WindowKey);
                SetText(worksheet.Cell(row, 1), key.SourceUserEmail ?? "未知用户");
                SetText(worksheet.Cell(row, 2), key.Name);
                SetText(worksheet.Cell(row, 3), key.ExternalId);
                SetText(worksheet.Cell(row, 4), key.Status);
                SetText(worksheet.Cell(row, 5), FormatTimestamp(key.LastUsedAt));
                SetText(worksheet.Cell(row, 6), FormatTimestamp(key.RetiredAt));
                WriteWindowIdentity(worksheet, row, 7, descriptor, window.WindowKey);
                SetInteger(worksheet.Cell(row, 12), window.Metrics.TotalRequests);
                SetInteger(worksheet.Cell(row, 13), window.Metrics.TotalInputTokens);
                SetInteger(worksheet.Cell(row, 14), window.Metrics.TotalOutputTokens);
                SetInteger(worksheet.Cell(row, 15), window.Metrics.TotalCacheCreationTokens);
                SetInteger(worksheet.Cell(row, 16), window.Metrics.TotalCacheReadTokens);
                SetInteger(worksheet.Cell(row, 17), window.Metrics.TotalTokens);
                SetCost(worksheet.Cell(row, 18), window.Metrics.TotalActualCost);
                SetCost(
                    worksheet.Cell(row, 19),
                    window.Metrics.TotalActualCost / Math.Max(descriptor?.DayCount ?? 0, 1));
                SetText(
                    worksheet.Cell(row, 20),
                    HasKeyWindowFailure(report, key, window.WindowKey)
                        ? "采集失败"
                        : "完整");
                row++;
            }
        }

        var lastRow = Math.Max(tableHeaderRow, row - 1);
        CreateTable(worksheet, tableHeaderRow, lastRow, headers.Length, "KeyUsageDetails");
        worksheet.SheetView.FreezeRows(tableHeaderRow);
        worksheet.SheetView.FreezeColumns(2);
        SetWidths(
            worksheet,
            24, 24, 20, 14, 23, 23, 22, 18, 13, 13, 10, 18, 22, 22, 25, 25, 22, 20, 25, 16);
        ConfigurePrint(worksheet, tableHeaderRow, landscape: true);
    }

    private static void AddUserSummarySheet(XLWorkbook workbook, ReportDocument report)
    {
        var worksheet = workbook.Worksheets.Add("用户汇总");
        ConfigureWorksheet(worksheet, XLColor.FromHtml("D9A441"));
        AddTitle(
            worksheet,
            "Sub2API 用户用量汇总",
            "每行表示一个 Sub2API 用户在一个统计窗口内汇总的全部 API Key 用量。");

        const int tableHeaderRow = 4;
        string[] headers =
        [
            "Sub2API 用户",
            "用户名",
            "用户 ID",
            "Key 数（个）",
            "窗口名称",
            "窗口类型",
            "开始日期",
            "结束日期",
            "天数",
            "请求数（次）",
            "输入 Token 数（个）",
            "输出 Token 数（个）",
            "缓存创建 Token 数（个）",
            "缓存读取 Token 数（个）",
            "总 Token 数（个）",
            "实际费用（USD）",
            "日均实际费用（USD/日）",
            "数据状态",
        ];
        WriteHeaders(worksheet, tableHeaderRow, headers);

        var row = tableHeaderRow + 1;
        foreach (var user in report.Users)
        {
            foreach (var window in OrderByWindows(report, user.Windows))
            {
                var descriptor = FindWindow(report, window.WindowKey);
                SetText(worksheet.Cell(row, 1), user.Email);
                SetText(worksheet.Cell(row, 2), user.Username ?? "未提供");
                SetInteger(worksheet.Cell(row, 3), user.ExternalUserId);
                SetInteger(worksheet.Cell(row, 4), user.KeyCount);
                WriteWindowIdentity(worksheet, row, 5, descriptor, window.WindowKey);
                SetInteger(worksheet.Cell(row, 10), window.Metrics.TotalRequests);
                SetInteger(worksheet.Cell(row, 11), window.Metrics.TotalInputTokens);
                SetInteger(worksheet.Cell(row, 12), window.Metrics.TotalOutputTokens);
                SetInteger(worksheet.Cell(row, 13), window.Metrics.TotalCacheCreationTokens);
                SetInteger(worksheet.Cell(row, 14), window.Metrics.TotalCacheReadTokens);
                SetInteger(worksheet.Cell(row, 15), window.Metrics.TotalTokens);
                SetCost(worksheet.Cell(row, 16), window.Metrics.TotalActualCost);
                SetCost(
                    worksheet.Cell(row, 17),
                    window.Metrics.TotalActualCost / Math.Max(descriptor?.DayCount ?? 0, 1));
                SetText(
                    worksheet.Cell(row, 18),
                    HasUserWindowFailure(report, user, window.WindowKey)
                        ? "部分采集失败"
                        : "完整");
                row++;
            }
        }

        var lastRow = Math.Max(tableHeaderRow, row - 1);
        CreateTable(worksheet, tableHeaderRow, lastRow, headers.Length, "UserUsageSummary");
        worksheet.SheetView.FreezeRows(tableHeaderRow);
        worksheet.SheetView.FreezeColumns(1);
        SetWidths(worksheet, 26, 20, 18, 14, 22, 18, 13, 13, 10, 18, 22, 22, 25, 25, 22, 20, 25, 18);
        ConfigurePrint(worksheet, tableHeaderRow, landscape: true);
    }

    private static void AddFailuresSheet(XLWorkbook workbook, ReportDocument report)
    {
        var worksheet = workbook.Worksheets.Add("采集异常");
        ConfigureWorksheet(worksheet, XLColor.FromHtml(WarningColor));
        AddTitle(
            worksheet,
            "采集异常明细",
            "以下窗口未成功采集，报告中对应 Key、用户及总计指标可能不完整。");

        const int tableHeaderRow = 4;
        string[] headers =
        [
            "用户 ID",
            "Sub2API 用户",
            "Key ID",
            "Key 名称",
            "窗口名称",
            "开始日期",
            "结束日期",
            "失败类型",
            "错误代码",
        ];
        WriteHeaders(worksheet, tableHeaderRow, headers);

        for (var index = 0; index < report.Diagnostics.FailedRanges.Count; index++)
        {
            var failure = report.Diagnostics.FailedRanges[index];
            var row = tableHeaderRow + 1 + index;
            SetInteger(worksheet.Cell(row, 1), failure.ExternalUserId);
            SetText(worksheet.Cell(row, 2), failure.UserEmail);
            SetInteger(worksheet.Cell(row, 3), failure.ExternalKeyId);
            SetText(worksheet.Cell(row, 4), failure.KeyName);
            var descriptor = FindWindow(report, failure.WindowKey);
            SetText(
                worksheet.Cell(row, 5),
                descriptor is null
                    ? failure.WindowKey
                    : ReportWindows.GetDisplayLabel(descriptor.Kind, descriptor.Label));
            SetDate(worksheet.Cell(row, 6), failure.StartDate);
            SetDate(worksheet.Cell(row, 7), failure.EndDateExclusive.AddDays(-1));
            SetText(
                worksheet.Cell(row, 8),
                failure.ErrorCode
                    ?? (failure.FailureKind is null
                        ? "未提供"
                        : DescribeFailureKindCode(failure.FailureKind.Value)));
            SetText(worksheet.Cell(row, 9), failure.ErrorCode ?? "未提供");
        }

        var lastRow = tableHeaderRow + report.Diagnostics.FailedRanges.Count;
        CreateTable(worksheet, tableHeaderRow, lastRow, headers.Length, "CollectionFailures");
        worksheet.SheetView.FreezeRows(tableHeaderRow);
        SetWidths(worksheet, 18, 26, 18, 24, 22, 13, 13, 18, 22);
        ConfigurePrint(worksheet, tableHeaderRow, landscape: true);
    }

    private static void AddDataNotesSheet(XLWorkbook workbook)
    {
        var worksheet = workbook.Worksheets.Add("数据说明");
        ConfigureWorksheet(worksheet, XLColor.FromHtml(MutedTextColor));
        AddTitle(
            worksheet,
            "数据与使用说明",
            "本文件可能包含个人用量信息，请勿附加到公开 Issue、日志或其他公开材料中。");

        const int tableHeaderRow = 4;
        string[] headers = ["项目", "说明"];
        WriteHeaders(worksheet, tableHeaderRow, headers);
        (string Item, string Description)[] notes =
        [
            ("统计边界", "开始日期与结束日期均为包含边界；工作簿由快照中的半开区间转换而来。"),
            ("实际费用", "以 USD 计价的实际费用；日均实际费用等于实际费用除以窗口自然日数。"),
            ("数据状态", "“完整”表示窗口未记录采集失败；“采集失败”或“部分采集失败”表示指标可能偏低。"),
            ("计数精度", "Excel 对超过 15 位的整数无法精确表示；此类请求数、Token 数和 ID 按文本保存以避免舍入。"),
            ("空值处理", "未提供的用户名、生命周期时间、失败类型和错误代码以明确文字标记，不使用空白表示。"),
            ("筛选与冻结", "明细表使用 Excel Table 并启用筛选；滚动时固定表头，宽表同时固定关键标识列。"),
            ("快照口径", "所有工作表均从同一不可变 canonical snapshot 生成，不会重新查询上游数据。"),
        ];
        for (var index = 0; index < notes.Length; index++)
        {
            var row = tableHeaderRow + 1 + index;
            SetText(worksheet.Cell(row, 1), notes[index].Item);
            SetText(worksheet.Cell(row, 2), notes[index].Description);
        }

        var lastRow = tableHeaderRow + notes.Length;
        CreateTable(worksheet, tableHeaderRow, lastRow, headers.Length, "DataNotes");
        worksheet.SheetView.FreezeRows(tableHeaderRow);
        worksheet.Column(1).Width = 20;
        worksheet.Column(2).Width = 84;
        worksheet.Range(tableHeaderRow + 1, 2, lastRow, 2).Style.Alignment.WrapText = true;
        worksheet.Rows(tableHeaderRow + 1, lastRow).Height = 34;
        ConfigurePrint(worksheet, tableHeaderRow, landscape: false);
    }

    private static void ConfigureWorksheet(IXLWorksheet worksheet, XLColor tabColor)
    {
        worksheet.TabColor = tabColor;
        worksheet.ShowGridLines = false;
        worksheet.Style.Font.FontName = "Aptos";
        worksheet.Style.Font.FontSize = 10.5;
        worksheet.Style.Font.FontColor = XLColor.FromHtml(TextColor);
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.RowHeight = 20;
    }

    private static void AddTitle(IXLWorksheet worksheet, string title, string description)
    {
        var titleCell = worksheet.Cell(1, 1);
        SetText(titleCell, title);
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 18;
        titleCell.Style.Font.FontColor = XLColor.FromHtml(HeaderColor);
        worksheet.Row(1).Height = 30;

        var descriptionCell = worksheet.Cell(2, 1);
        SetText(descriptionCell, description);
        descriptionCell.Style.Font.FontColor = XLColor.FromHtml(MutedTextColor);
        descriptionCell.Style.Alignment.WrapText = true;
        worksheet.Row(2).Height = 30;
    }

    private static void StyleSectionHeading(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 12;
        cell.Style.Font.FontColor = XLColor.FromHtml(HeaderColor);
    }

    private static void WriteHeaders(IXLWorksheet worksheet, int row, string[] headers)
    {
        for (var index = 0; index < headers.Length; index++)
        {
            SetText(worksheet.Cell(row, index + 1), headers[index]);
        }
    }

    private static void CreateTable(
        IXLWorksheet worksheet,
        int headerRow,
        int lastRow,
        int lastColumn,
        string tableName)
    {
        var table = worksheet.Range(headerRow, 1, lastRow, lastColumn).CreateTable(tableName);
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
        table.ShowRowStripes = true;

        var header = worksheet.Range(headerRow, 1, headerRow, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderColor);
        header.Style.Alignment.WrapText = true;
        worksheet.Row(headerRow).Height = 34;

        if (lastRow > headerRow)
        {
            var body = worksheet.Range(headerRow + 1, 1, lastRow, lastColumn);
            body.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            body.Style.Border.BottomBorderColor = XLColor.FromHtml("D9E1E5");
        }
    }

    private static void ConfigurePrint(IXLWorksheet worksheet, int repeatedHeaderRow, bool landscape)
    {
        worksheet.PageSetup.PageOrientation = landscape
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PagesTall = 0;
        worksheet.PageSetup.SetRowsToRepeatAtTop(repeatedHeaderRow, repeatedHeaderRow);
        worksheet.PageSetup.Margins.Top = 0.5;
        worksheet.PageSetup.Margins.Bottom = 0.5;
        worksheet.PageSetup.Margins.Left = 0.35;
        worksheet.PageSetup.Margins.Right = 0.35;
    }

    private static void SetWidths(IXLWorksheet worksheet, params double[] widths)
    {
        for (var index = 0; index < widths.Length; index++)
        {
            worksheet.Column(index + 1).Width = widths[index];
        }
    }

    private static void WriteWindowIdentity(
        IXLWorksheet worksheet,
        int row,
        int startColumn,
        ReportWindowDescriptor? descriptor,
        string windowKey)
    {
        SetText(
            worksheet.Cell(row, startColumn),
            descriptor is null
                ? windowKey
                : ReportWindows.GetDisplayLabel(descriptor.Kind, descriptor.Label));
        SetText(
            worksheet.Cell(row, startColumn + 1),
            descriptor is null
                ? "未知"
                : GetWindowKindLabel(descriptor.Kind));
        if (descriptor is null)
        {
            SetText(worksheet.Cell(row, startColumn + 2), "未提供");
            SetText(worksheet.Cell(row, startColumn + 3), "未提供");
            SetInteger(worksheet.Cell(row, startColumn + 4), 0);
            return;
        }

        SetDate(worksheet.Cell(row, startColumn + 2), descriptor.StartDate);
        SetDate(worksheet.Cell(row, startColumn + 3), descriptor.EndDateExclusive.AddDays(-1));
        SetInteger(worksheet.Cell(row, startColumn + 4), descriptor.DayCount);
    }

    private static void SetCellValue(IXLCell cell, object value)
    {
        switch (value)
        {
            case int intValue:
                SetInteger(cell, intValue);
                break;
            case long longValue:
                SetInteger(cell, longValue);
                break;
            default:
                SetText(cell, value.ToString() ?? string.Empty);
                break;
        }
    }

    /// <summary>
    /// 将不受信文本作为显式 <see cref="XLDataType.Text"/> 写入单元格。ClosedXML 0.105
    /// 对普通字符串一律保存为文本，不会把以 <c>=</c>、<c>+</c>、<c>-</c>、<c>@</c>
    /// 开头的内容解释为公式；此处保留防御性校验，避免上游行为变化导致公式注入。
    /// </summary>
    private static void SetText(IXLCell cell, string value)
    {
        cell.Value = value;
        if (cell.HasFormula || cell.DataType != XLDataType.Text)
        {
            throw new InvalidOperationException(
                $"单元格 {cell.Address} 的不受信文本被解释为 {cell.DataType}，拒绝写出。");
        }
    }

    private static void SetInteger(IXLCell cell, long value)
    {
        if (Math.Abs((decimal)value) > MaximumExactExcelInteger)
        {
            SetText(cell, value.ToString(CultureInfo.InvariantCulture));
            cell.Style.NumberFormat.Format = "@";
            return;
        }

        cell.Value = value;
        cell.Style.NumberFormat.Format = IntegerFormat;
    }

    private static void SetCost(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = CostFormat;
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = DateFormat;
    }

    private static ReportWindowDescriptor? FindWindow(ReportDocument report, string windowKey) =>
        report.Windows.FirstOrDefault(window => window.Key == windowKey);

    private static ReportWindowMetrics[] OrderByWindows(
        ReportDocument report,
        IReadOnlyList<ReportWindowMetrics> windows)
    {
        var order = report.Windows
            .Select((descriptor, index) => (descriptor.Key, index))
            .ToDictionary(pair => pair.Key, pair => pair.index);
        return windows
            .OrderBy(window => order.TryGetValue(window.WindowKey, out var index) ? index : int.MaxValue)
            .ToArray();
    }

    private static bool HasWindowFailure(ReportDocument report, string windowKey) =>
        report.Diagnostics.FailedRanges.Any(failure => failure.WindowKey == windowKey);

    private static bool HasKeyWindowFailure(
        ReportDocument report,
        ReportKeyUsage key,
        string windowKey) => report.Diagnostics.FailedRanges.Any(failure =>
            failure.WindowKey == windowKey
            && failure.ExternalKeyId.ToString(CultureInfo.InvariantCulture) == key.ExternalId
            && (key.SourceUserId is null || failure.ExternalUserId == key.SourceUserId));

    private static bool HasUserWindowFailure(
        ReportDocument report,
        ReportUserUsage user,
        string windowKey) => report.Diagnostics.FailedRanges.Any(failure =>
            failure.WindowKey == windowKey && failure.ExternalUserId == user.ExternalUserId);

    private static string DescribeFailureKindCode(Sub2ApiFailureKind kind) => kind switch
    {
        Sub2ApiFailureKind.Unauthorized => "unauthorized",
        Sub2ApiFailureKind.Forbidden => "forbidden",
        Sub2ApiFailureKind.Incompatible => "incompatible",
        Sub2ApiFailureKind.RateLimited => "rate-limited",
        Sub2ApiFailureKind.Timeout => "timeout",
        Sub2ApiFailureKind.Unavailable => "unavailable",
        _ => "invalid-response",
    };

    private static string FormatTimestamp(DateTimeOffset? value) => value?.ToString(
        "yyyy-MM-dd HH:mm:ss zzz",
        CultureInfo.InvariantCulture) ?? "不适用";

    private static string GetStatusLabel(ReportStatus status) => status switch
    {
        ReportStatus.Complete => "完整",
        ReportStatus.Partial => "部分完成",
        _ => status.ToString(),
    };

    private static string GetWindowKindLabel(ReportWindowKind kind) => kind switch
    {
        ReportWindowKind.RollingDays => "滚动自然日",
        ReportWindowKind.PreviousCalendarWeek => "上一自然周",
        ReportWindowKind.PreviousCalendarMonth => "上一自然月",
        ReportWindowKind.CustomRange => "自定义区间",
        _ => kind.ToString(),
    };
}
