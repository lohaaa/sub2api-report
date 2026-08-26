using System.Globalization;
using System.Text;
using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.Infrastructure.Reports;

internal static class ReportCsvSerializer
{
    public static byte[] Serialize(ReportDocument report)
    {
        var builder = new StringBuilder();
        AppendRow(builder, "报告 ID", report.ReportId.ToString("D"));
        AppendRow(builder, "状态", report.Status.ToString());
        AppendRow(builder, "生成时间", report.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        AppendRow(builder, "时区", report.Timezone);
        AppendRow(builder, "7 日窗口", $"{FormatDate(report.SevenDayWindow.StartDate)} 至 {FormatDate(report.SevenDayWindow.EndDate)}");
        AppendRow(builder, "30 日窗口", $"{FormatDate(report.ThirtyDayWindow.StartDate)} 至 {FormatDate(report.ThirtyDayWindow.EndDate)}");
        AppendRow(builder, "失败区间", report.Diagnostics.FailedSegments.Count.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "未归属区间", report.Diagnostics.UnassignedSegments.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("\r\n");
        AppendRow(
            builder,
            "人员编码",
            "人员",
            "Key 数量",
            "7 日请求数",
            "7 日输入 Token",
            "7 日输出 Token",
            "7 日缓存创建 Token",
            "7 日缓存读取 Token",
            "7 日总 Token",
            "7 日标准费用",
            "7 日实际费用",
            "30 日请求数",
            "30 日输入 Token",
            "30 日输出 Token",
            "30 日缓存创建 Token",
            "30 日缓存读取 Token",
            "30 日总 Token",
            "30 日标准费用",
            "30 日实际费用",
            "30 日日均实际费用");

        foreach (var person in report.People)
        {
            AppendUsageRow(builder, person.Code, person.DisplayName, person.KeyCount, person.SevenDay, person.ThirtyDay);
        }

        AppendUsageRow(
            builder,
            "TOTAL",
            "全员总计",
            report.Keys.Count,
            report.SevenDayTotal,
            report.ThirtyDayTotal);

        var content = Encoding.UTF8.GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var output = new byte[preamble.Length + content.Length];
        preamble.CopyTo(output, 0);
        content.CopyTo(output, preamble.Length);
        return output;
    }

    private static void AppendUsageRow(
        StringBuilder builder,
        string code,
        string displayName,
        int keyCount,
        ReportUsageMetrics sevenDay,
        ReportUsageMetrics thirtyDay) => AppendRow(
            builder,
            code,
            displayName,
            keyCount.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalRequests.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalInputTokens.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalOutputTokens.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalCacheCreationTokens.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalCacheReadTokens.ToString(CultureInfo.InvariantCulture),
            sevenDay.TotalTokens.ToString(CultureInfo.InvariantCulture),
            FormatDecimal(sevenDay.TotalCost),
            FormatDecimal(sevenDay.TotalActualCost),
            thirtyDay.TotalRequests.ToString(CultureInfo.InvariantCulture),
            thirtyDay.TotalInputTokens.ToString(CultureInfo.InvariantCulture),
            thirtyDay.TotalOutputTokens.ToString(CultureInfo.InvariantCulture),
            thirtyDay.TotalCacheCreationTokens.ToString(CultureInfo.InvariantCulture),
            thirtyDay.TotalCacheReadTokens.ToString(CultureInfo.InvariantCulture),
            thirtyDay.TotalTokens.ToString(CultureInfo.InvariantCulture),
            FormatDecimal(thirtyDay.TotalCost),
            FormatDecimal(thirtyDay.TotalActualCost),
            FormatDecimal(thirtyDay.TotalActualCost / 30m));

    private static void AppendRow(StringBuilder builder, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(values[index]));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string value)
    {
        var protectedValue = value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return protectedValue.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{protectedValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : protectedValue;
    }

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
