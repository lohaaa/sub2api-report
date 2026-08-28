using System.Globalization;
using System.Text;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

/// <summary>Builds the shared neutral-language report lines used by every channel.</summary>
internal static class ReportMessageRenderer
{
    public static string BuildSubject(ReportDocument report)
    {
        var fallbackDate = DateOnly.FromDateTime(report.GeneratedAt.UtcDateTime);
        var start = report.Windows.Count == 0
            ? fallbackDate
            : report.Windows.Min(window => window.StartDate);
        var end = report.Windows.Count == 0
            ? fallbackDate
            : report.Windows.Max(window => window.EndDateExclusive).AddDays(-1);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[Codex 用量报告] {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}");
    }

    /// <summary>Returns deterministic plain-text lines; each line stays on its own message row.</summary>
    public static IReadOnlyList<string> BuildLines(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            $"统计时区：{report.Timezone}（不含报告日当天）；窗口数（个） {report.Windows.Count}",
        };
        foreach (var window in report.Windows)
        {
            lines.Add(
                $"窗口 {window.Key}（{window.Label}）：{window.StartDate:yyyy-MM-dd} ~ "
                + $"{window.EndDateExclusive.AddDays(-1):yyyy-MM-dd}，共 {window.DayCount} 天");
        }

        foreach (var total in report.WindowTotals)
        {
            lines.Add(
                $"窗口 {total.WindowKey} 合计：请求数（次） {total.Metrics.TotalRequests:N0}；"
                + $"Token 数（个） {total.Metrics.TotalTokens:N0}；"
                + $"实际费用（USD） {FormatCost(total.Metrics.TotalActualCost)}");
        }

        if (report.Users.Count > 0)
        {
            lines.Add("Sub2API 用户明细：");
            foreach (var user in report.Users)
            {
                var details = string.Join(
                    "；",
                    user.Windows.Select(window =>
                        $"窗口 {window.WindowKey} 实际费用（USD） {FormatCost(window.Metrics.TotalActualCost)}"
                        + $"（请求数（次） {window.Metrics.TotalRequests:N0}，"
                        + $"Token 数（个） {window.Metrics.TotalTokens:N0}）"));
                lines.Add($"- {user.Email}（Key 数（个） {user.KeyCount}）：{details}");
            }
        }

        if (report.Diagnostics.FailedRanges.Count > 0)
        {
            lines.Add($"⚠ 数据不完整：采集失败区间数（个） {report.Diagnostics.FailedRanges.Count}");
        }

        if (report.Status == ReportStatus.Partial)
        {
            lines.Add("⚠ 报告状态为部分完成，仅供参考。");
        }

        lines.Add($"报告编号 {report.ReportId:D}；生成时间 {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        return lines;
    }

    public static string BuildHtmlBody(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder();
        builder.Append("<p>统计时区：<strong>").Append(Escape(report.Timezone))
            .Append("</strong>（不含报告日当天），窗口数（个） ")
            .Append(report.Windows.Count.ToString(CultureInfo.InvariantCulture))
            .Append("。</p>");
        builder.Append("<ul>");
        foreach (var window in report.Windows)
        {
            builder.Append("<li>窗口 ").Append(Escape(window.Key))
                .Append('（').Append(Escape(window.Label)).Append("）：")
                .Append(CultureInfo.InvariantCulture, $"{window.StartDate:yyyy-MM-dd} ~ ")
                .Append(CultureInfo.InvariantCulture, $"{window.EndDateExclusive.AddDays(-1):yyyy-MM-dd}")
                .Append("，共 ")
                .Append(window.DayCount.ToString(CultureInfo.InvariantCulture))
                .Append(" 天</li>");
        }

        builder.Append("</ul>");
        builder.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\">");
        builder.Append("<thead><tr><th>Sub2API 用户</th><th>Key 数（个）</th>");
        foreach (var window in report.Windows)
        {
            builder.Append("<th>").Append(Escape(window.Key))
                .Append(" 请求数（次）</th><th>")
                .Append(Escape(window.Key))
                .Append(" 实际费用（USD）</th>");
        }

        builder.Append("</tr></thead><tbody>");
        foreach (var user in report.Users)
        {
            builder.Append("<tr><td>").Append(Escape(user.Email))
                .Append("</td><td>")
                .Append(user.KeyCount.ToString(CultureInfo.InvariantCulture)).Append("</td>");
            foreach (var window in report.Windows)
            {
                var metrics = user.Windows
                    .FirstOrDefault(item => item.WindowKey == window.Key)
                    ?.Metrics;
                builder.Append("<td>")
                    .Append((metrics?.TotalRequests ?? 0).ToString("N0", CultureInfo.InvariantCulture))
                    .Append("</td><td>")
                    .Append(FormatCost(metrics?.TotalActualCost ?? 0m))
                    .Append("</td>");
            }

            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        if (report.Diagnostics.FailedRanges.Count > 0
            || report.Status == ReportStatus.Partial)
        {
            builder.Append("<p><strong>⚠ 数据不完整</strong>：采集失败区间数（个） ")
                .Append(report.Diagnostics.FailedRanges.Count.ToString(CultureInfo.InvariantCulture))
                .Append("。报告状态为")
                .Append(report.Status == ReportStatus.Partial ? "部分完成" : "完整")
                .Append("，请登录系统查看详情。</p>");
        }

        builder.Append("<p>报告编号 ").Append(report.ReportId.ToString("D"))
            .Append("；生成时间 ")
            .Append(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC。CSV 附件包含完整明细。</p>");
        return builder.ToString();
    }

    private static string FormatCost(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        global::System.Net.WebUtility.HtmlEncode(value);
}
