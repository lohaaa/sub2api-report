using System.Globalization;
using System.Text;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

/// <summary>Builds the shared neutral-language report lines used by every channel.</summary>
internal static class ReportMessageRenderer
{
    private const string CurrencySymbol = "¥";

    public static string BuildSubject(ReportDocument report) => string.Create(
        CultureInfo.InvariantCulture,
        $"[Codex 用量报告] {report.ThirtyDayWindow.StartDate:yyyy-MM-dd} 至 {report.ThirtyDayWindow.EndDate:yyyy-MM-dd}");

    /// <summary>Returns deterministic plain-text lines; each line stays on its own message row.</summary>
    public static IReadOnlyList<string> BuildLines(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            $"统计窗口：{report.ThirtyDayWindow.StartDate:yyyy-MM-dd} ~ {report.ThirtyDayWindow.EndDate:yyyy-MM-dd}（{report.Timezone}，不含报告日当天）",
            $"30 天合计：请求 {report.ThirtyDayTotal.TotalRequests:N0} 次；Tokens {report.ThirtyDayTotal.TotalTokens:N0}；实际费用 {FormatCost(report.ThirtyDayTotal.TotalActualCost)}",
            $"7 天合计：请求 {report.SevenDayTotal.TotalRequests:N0} 次；Tokens {report.SevenDayTotal.TotalTokens:N0}；实际费用 {FormatCost(report.SevenDayTotal.TotalActualCost)}",
        };
        if (report.Users.Count > 0)
        {
            lines.Add("Sub2API 用户明细：");
            foreach (var user in report.Users)
            {
                lines.Add(
                    $"- {user.Email}（{user.KeyCount} 个 Key）："
                    + $"7 天 {FormatCost(user.SevenDay.TotalActualCost)}（请求 {user.SevenDay.TotalRequests:N0} 次）；"
                    + $"30 天 {FormatCost(user.ThirtyDay.TotalActualCost)}"
                    + $"（请求 {user.ThirtyDay.TotalRequests:N0} 次，Tokens {user.ThirtyDay.TotalTokens:N0}）");
            }
        }

        if (report.Diagnostics.FailedRanges.Count > 0)
        {
            lines.Add($"⚠ 数据不完整：{report.Diagnostics.FailedRanges.Count} 个采集区间失败");
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
        builder.Append("<p>统计窗口：<strong>")
            .Append(CultureInfo.InvariantCulture, $"{report.ThirtyDayWindow.StartDate:yyyy-MM-dd} ~ {report.ThirtyDayWindow.EndDate:yyyy-MM-dd}")
            .Append("</strong>（")
            .Append(report.Timezone)
            .Append("，不含报告日当天）</p>");
        builder.Append("<p>7 天合计：请求 ")
            .Append(report.SevenDayTotal.TotalRequests.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" 次，费用 ")
            .Append(FormatCost(report.SevenDayTotal.TotalActualCost))
            .Append("；30 天合计：请求 ")
            .Append(report.ThirtyDayTotal.TotalRequests.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" 次，Tokens ")
            .Append(report.ThirtyDayTotal.TotalTokens.ToString("N0", CultureInfo.InvariantCulture))
            .Append("，费用 ")
            .Append(FormatCost(report.ThirtyDayTotal.TotalActualCost))
            .Append("</p>");
        builder.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\">");
        builder.Append("<thead><tr><th>Sub2API 用户</th><th>Key 数</th><th>7 天请求</th><th>7 天费用</th>")
            .Append("<th>30 天请求</th><th>30 天 Tokens</th><th>30 天费用</th></tr></thead><tbody>");
        foreach (var user in report.Users)
        {
            builder.Append("<tr><td>").Append(Escape(user.Email))
                .Append("</td><td>")
                .Append(user.KeyCount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(user.SevenDay.TotalRequests.ToString("N0", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(FormatCost(user.SevenDay.TotalActualCost)).Append("</td><td>")
                .Append(user.ThirtyDay.TotalRequests.ToString("N0", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(user.ThirtyDay.TotalTokens.ToString("N0", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(FormatCost(user.ThirtyDay.TotalActualCost)).Append("</td></tr>");
        }

        builder.Append("</tbody></table>");
        if (report.Diagnostics.FailedRanges.Count > 0
            || report.Status == ReportStatus.Partial)
        {
            builder.Append("<p><strong>⚠ 数据不完整</strong>：采集失败 ")
                .Append(report.Diagnostics.FailedRanges.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 个区间。报告状态为")
                .Append(report.Status == ReportStatus.Partial ? "部分完成" : "完整")
                .Append("，请登录系统查看详情。</p>");
        }

        builder.Append("<p>报告编号 ").Append(report.ReportId.ToString("D"))
            .Append("；生成时间 ")
            .Append(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC。CSV 附件包含完整明细。</p>");
        return builder.ToString();
    }

    private static string FormatCost(decimal value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{CurrencySymbol}{value:0.####}");

    private static string Escape(string value) =>
        global::System.Net.WebUtility.HtmlEncode(value);
}
