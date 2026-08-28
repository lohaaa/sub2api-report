using System.Globalization;
using System.Text;
using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.Infrastructure.Reports;

internal static class ReportCsvSerializer
{
    public static byte[] Serialize(ReportDocument report)
    {
        var builder = new StringBuilder();
        AppendRow(
            builder,
            "Sub2API 用户",
            "Key 名称",
            "Key ID",
            "状态",
            "窗口 Key",
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
            "日均实际费用（USD/日）");

        var windowOrder = report.Windows
            .Select((descriptor, index) => (descriptor.Key, index))
            .ToDictionary(pair => pair.Key, pair => pair.index);

        foreach (var key in report.Keys)
        {
            foreach (var window in OrderByWindows(windowOrder, key.Windows))
            {
                AppendUsageRow(
                    builder,
                    key.SourceUserEmail ?? "未知用户",
                    key.Name,
                    key.ExternalId,
                    key.Status,
                    report,
                    window.WindowKey,
                    window.Metrics);
            }
        }

        foreach (var user in report.Users)
        {
            foreach (var window in OrderByWindows(windowOrder, user.Windows))
            {
                AppendUsageRow(
                    builder,
                    user.Email,
                    "（用户小计）",
                    string.Empty,
                    string.Empty,
                    report,
                    window.WindowKey,
                    window.Metrics);
            }
        }

        foreach (var total in report.WindowTotals)
        {
            AppendUsageRow(
                builder,
                "TOTAL",
                "全部总计",
                string.Empty,
                string.Empty,
                report,
                total.WindowKey,
                total.Metrics);
        }

        var content = Encoding.UTF8.GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var output = new byte[preamble.Length + content.Length];
        preamble.CopyTo(output, 0);
        content.CopyTo(output, preamble.Length);
        return output;
    }

    private static ReportWindowMetrics[] OrderByWindows(
        Dictionary<string, int> windowOrder,
        IReadOnlyList<ReportWindowMetrics> windows) => windows
            .OrderBy(window => windowOrder.TryGetValue(window.WindowKey, out var index)
                ? index
                : int.MaxValue)
            .ToArray();

    private static void AppendUsageRow(
        StringBuilder builder,
        string email,
        string name,
        string externalId,
        string status,
        ReportDocument report,
        string windowKey,
        ReportUsageMetrics metrics)
    {
        var descriptor = report.Windows.FirstOrDefault(window => window.Key == windowKey);
        AppendRow(
            builder,
            email,
            name,
            externalId,
            status,
            windowKey,
            descriptor is null
                ? string.Empty
                : ReportWindows.GetDisplayLabel(descriptor.Kind, descriptor.Label),
            descriptor?.Kind.ToString() ?? string.Empty,
            descriptor is null ? string.Empty : FormatDate(descriptor.StartDate),
            descriptor is null ? string.Empty : FormatDate(descriptor.EndDateExclusive.AddDays(-1)),
            descriptor is null ? string.Empty : descriptor.DayCount.ToString(CultureInfo.InvariantCulture),
            metrics.TotalRequests.ToString(CultureInfo.InvariantCulture),
            metrics.TotalInputTokens.ToString(CultureInfo.InvariantCulture),
            metrics.TotalOutputTokens.ToString(CultureInfo.InvariantCulture),
            metrics.TotalCacheCreationTokens.ToString(CultureInfo.InvariantCulture),
            metrics.TotalCacheReadTokens.ToString(CultureInfo.InvariantCulture),
            metrics.TotalTokens.ToString(CultureInfo.InvariantCulture),
            FormatDecimal(metrics.TotalActualCost),
            FormatDecimal(metrics.TotalActualCost / Math.Max(descriptor?.DayCount ?? 0, 1)));
    }

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
