using System.Globalization;
using System.Text;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

/// <summary>Renders channel-specific report summaries from the canonical report document.</summary>
internal static class ReportMessageRenderer
{
    public static string BuildSubject(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var (start, end) = GetDateRange(report);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[Codex 用量报告] {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}");
    }

    public static IReadOnlyList<string> BuildDingTalkLines(
        ReportDocument report,
        string? reportDownloadUrl = null,
        string? reportDownloadPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "### Codex 用量摘要",
            $"> {BuildStatusLabel(report)} · 统计时区 {EscapeMarkdown(report.Timezone)} · "
                + $"{report.Windows.Count} 个窗口 · {report.Users.Count} 个用户 · {report.Keys.Count} 个 Key",
        };

        if (report.Status == ReportStatus.Partial || report.Diagnostics.FailedRanges.Count > 0)
        {
            lines.Add(
                $"> **数据不完整**：{report.Diagnostics.FailedRanges.Count} 个采集区间失败，报告仅供参考。");
        }

        foreach (var window in report.Windows)
        {
            var total = GetMetrics(report.WindowTotals, window.Key);
            lines.Add($"### {EscapeMarkdown(GetWindowLabel(window))}");
            lines.Add($"> {FormatWindowRange(window)}");
            lines.Add($"- 请求数（次）：**{FormatCount(total.TotalRequests)}**");
            lines.Add($"- Token 数（个）：**{FormatCount(total.TotalTokens)}**");
            lines.Add($"- 实际费用（USD）：**{FormatCost(total.TotalActualCost)}**");
        }

        AppendDingTalkUsers(lines, report);
        lines.Add("---");
        lines.Add(reportDownloadUrl is null
            ? "完整 Key 明细请在 Sub2API Report 中下载 XLSX 工作簿。"
            : $"[下载 XLSX 完整明细（{reportDownloadPolicy ?? "限时授权"}）]({reportDownloadUrl})");
        lines.Add(BuildFooter(report));
        return lines;
    }

    public static IReadOnlyList<string> BuildFeishuLines(
        ReportDocument report,
        string? reportDownloadUrl = null,
        string? reportDownloadPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "Codex 用量摘要",
            $"{BuildStatusLabel(report)}｜统计时区 {report.Timezone}｜{report.Windows.Count} 个窗口｜"
                + $"{report.Users.Count} 个用户｜{report.Keys.Count} 个 Key",
        };

        if (report.Status == ReportStatus.Partial || report.Diagnostics.FailedRanges.Count > 0)
        {
            lines.Add(
                $"数据不完整：{report.Diagnostics.FailedRanges.Count} 个采集区间失败，报告仅供参考。");
        }

        foreach (var window in report.Windows)
        {
            var total = GetMetrics(report.WindowTotals, window.Key);
            lines.Add($"【{GetWindowLabel(window)}】{FormatWindowRange(window)}");
            lines.Add(
                $"合计｜请求数（次） {FormatCount(total.TotalRequests)}｜"
                + $"Token 数（个） {FormatCount(total.TotalTokens)}｜"
                + $"实际费用（USD） {FormatCost(total.TotalActualCost)}");
        }

        AppendFeishuUsers(lines, report);
        lines.Add(reportDownloadUrl is null
            ? "完整 Key 明细请在 Sub2API Report 中下载 XLSX 工作簿。"
            : $"下载 XLSX 完整明细（{reportDownloadPolicy ?? "限时授权"}）：{reportDownloadUrl}");
        lines.Add(BuildFooter(report));
        return lines;
    }

    public static string BuildHtmlBody(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var (start, end) = GetDateRange(report);
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html lang=\"zh-CN\"><body style=\"margin:0;background:#f4f4f5;")
            .Append("font-family:Arial,'Microsoft YaHei',sans-serif;color:#27272a;\">")
            .Append("<div role=\"article\" aria-roledescription=\"电子邮件\" aria-label=\"Codex 用量报告\"")
            .Append(" style=\"padding:24px 12px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"")
            .Append(" style=\"max-width:760px;margin:0 auto;border-collapse:separate;background:#ffffff;")
            .Append("border:1px solid #e4e4e7;border-radius:8px;overflow:hidden;\"><tr><td>");

        builder.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"")
            .Append(" style=\"border-collapse:collapse;background:#18181b;color:#ffffff;\"><tr><td")
            .Append(" style=\"padding:28px 32px;border-top:4px solid #0f766e;\">")
            .Append("<p style=\"margin:0 0 8px;color:#99f6e4;font-size:12px;font-weight:700;\">")
            .Append("SUB2API REPORT</p><h1 style=\"margin:0;font-size:24px;line-height:1.3;font-weight:700;\">")
            .Append("Codex 用量报告</h1><p style=\"margin:10px 0 0;color:#d4d4d8;font-size:14px;\">")
            .Append(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}")
            .Append(" · ").Append(Escape(report.Timezone)).Append(" · ")
            .Append(Escape(BuildStatusLabel(report))).Append("</p></td></tr></table>");

        builder.Append("<div style=\"padding:28px 32px;\">");
        if (report.Status == ReportStatus.Partial || report.Diagnostics.FailedRanges.Count > 0)
        {
            builder.Append("<div style=\"margin:0 0 24px;padding:14px 16px;border-left:4px solid #b91c1c;")
                .Append("background:#fef2f2;color:#7f1d1d;font-size:13px;line-height:1.6;\">")
                .Append("<strong>数据不完整</strong><br>")
                .Append(report.Diagnostics.FailedRanges.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 个采集区间失败，本报告仅供参考。请登录系统查看失败详情。</div>");
        }

        AppendOverview(builder, report);
        foreach (var window in report.Windows)
        {
            AppendWindowSection(builder, report, window);
        }

        builder.Append("<div style=\"margin-top:28px;padding-top:20px;border-top:1px solid #e4e4e7;")
            .Append("color:#71717a;font-size:12px;line-height:1.7;\">")
            .Append("<p style=\"margin:0 0 6px;\"><strong style=\"color:#3f3f46;\">附件</strong>：")
            .Append("XLSX 工作簿包含全部 Key、窗口和费用明细，可直接使用 Excel 或 WPS 打开。</p><p style=\"margin:0;\">")
            .Append("报告编号 ").Append(report.ReportId.ToString("D"))
            .Append("<br>生成时间 ")
            .Append(report.GeneratedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC</p></div></div></td></tr></table></div></body></html>");
        return builder.ToString();
    }

    private static void AppendDingTalkUsers(List<string> lines, ReportDocument report)
    {
        if (report.Users.Count == 0)
        {
            return;
        }

        lines.Add("### Sub2API 用户明细");
        foreach (var user in report.Users.OrderBy(item => item.Email, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- **{EscapeMarkdown(user.Email)}**（Key 数（个） {user.KeyCount}）");
            foreach (var window in report.Windows)
            {
                var metrics = GetMetrics(user.Windows, window.Key);
                lines.Add(
                    $"  - {EscapeMarkdown(GetWindowLabel(window))}：请求数（次） {FormatCount(metrics.TotalRequests)}；"
                    + $"Token 数（个） {FormatCount(metrics.TotalTokens)}；"
                    + $"实际费用（USD） {FormatCost(metrics.TotalActualCost)}");
            }
        }
    }

    private static void AppendFeishuUsers(List<string> lines, ReportDocument report)
    {
        if (report.Users.Count == 0)
        {
            return;
        }

        lines.Add("Sub2API 用户明细");
        var index = 1;
        foreach (var user in report.Users.OrderBy(item => item.Email, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{index}. {user.Email}（Key 数（个） {user.KeyCount}）");
            foreach (var window in report.Windows)
            {
                var metrics = GetMetrics(user.Windows, window.Key);
                lines.Add(
                    $"   {GetWindowLabel(window)}｜请求数（次） {FormatCount(metrics.TotalRequests)}｜"
                    + $"Token 数（个） {FormatCount(metrics.TotalTokens)}｜"
                    + $"实际费用（USD） {FormatCost(metrics.TotalActualCost)}");
            }

            index++;
        }
    }

    private static void AppendOverview(StringBuilder builder, ReportDocument report)
    {
        builder.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"")
            .Append(" style=\"margin:0 0 28px;border-collapse:collapse;\"><tr>");
        AppendOverviewCell(builder, "统计窗口", report.Windows.Count);
        AppendOverviewCell(builder, "Sub2API 用户", report.Users.Count);
        AppendOverviewCell(builder, "API Key", report.Keys.Count);
        builder.Append("</tr></table>");
    }

    private static void AppendOverviewCell(StringBuilder builder, string label, int value)
    {
        builder.Append("<td width=\"33.33%\" style=\"padding:0 12px 0 0;vertical-align:top;\">")
            .Append("<span style=\"display:block;color:#71717a;font-size:12px;\">")
            .Append(Escape(label)).Append("</span><strong style=\"display:block;margin-top:4px;")
            .Append("font-size:22px;line-height:1.2;color:#18181b;\">")
            .Append(value.ToString(CultureInfo.InvariantCulture)).Append("</strong></td>");
    }

    private static void AppendWindowSection(
        StringBuilder builder,
        ReportDocument report,
        ReportWindowDescriptor window)
    {
        var total = GetMetrics(report.WindowTotals, window.Key);
        builder.Append("<section style=\"margin:0 0 28px;\"><div style=\"margin-bottom:12px;\">")
            .Append("<h2 style=\"margin:0;font-size:17px;line-height:1.4;color:#18181b;\">")
            .Append(Escape(GetWindowLabel(window))).Append("</h2><p style=\"margin:4px 0 0;color:#71717a;font-size:12px;\">")
            .Append(Escape(FormatWindowRange(window))).Append("</p></div>");

        builder.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"")
            .Append(" style=\"margin-bottom:14px;border-collapse:collapse;background:#f8fafc;")
            .Append("border:1px solid #e2e8f0;\"><tr>");
        AppendMetricCell(builder, "请求数（次）", FormatCount(total.TotalRequests));
        AppendMetricCell(builder, "Token 数（个）", FormatCount(total.TotalTokens));
        AppendMetricCell(builder, "实际费用（USD）", FormatCost(total.TotalActualCost));
        builder.Append("</tr></table>");

        builder.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"")
            .Append(" style=\"border-collapse:collapse;border:1px solid #e4e4e7;font-size:12px;\">")
            .Append("<thead><tr style=\"background:#fafafa;color:#52525b;\">")
            .Append("<th scope=\"col\" style=\"padding:9px 10px;text-align:left;border-bottom:1px solid #e4e4e7;\">")
            .Append("Sub2API 用户</th><th scope=\"col\" style=\"padding:9px 10px;text-align:right;")
            .Append("border-bottom:1px solid #e4e4e7;\">Key 数（个）</th>")
            .Append("<th scope=\"col\" style=\"padding:9px 10px;text-align:right;border-bottom:1px solid #e4e4e7;\">")
            .Append("请求数（次）</th><th scope=\"col\" style=\"padding:9px 10px;text-align:right;")
            .Append("border-bottom:1px solid #e4e4e7;\">Token 数（个）</th>")
            .Append("<th scope=\"col\" style=\"padding:9px 10px;text-align:right;border-bottom:1px solid #e4e4e7;\">")
            .Append("实际费用（USD）</th></tr></thead><tbody>");

        var users = report.Users
            .Select(user => (User: user, Metrics: GetMetrics(user.Windows, window.Key)))
            .OrderByDescending(item => item.Metrics.TotalActualCost)
            .ThenBy(item => item.User.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (users.Length == 0)
        {
            builder.Append("<tr><td colspan=\"5\" style=\"padding:14px 10px;text-align:center;color:#71717a;\">")
                .Append("当前窗口没有用户用量</td></tr>");
        }
        else
        {
            foreach (var (user, metrics) in users)
            {
                builder.Append("<tr><td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;")
                    .Append("word-break:break-word;\">").Append(Escape(user.Email)).Append("</td>");
                AppendNumericCell(builder, user.KeyCount.ToString(CultureInfo.InvariantCulture));
                AppendNumericCell(builder, FormatCount(metrics.TotalRequests));
                AppendNumericCell(builder, FormatCount(metrics.TotalTokens));
                AppendNumericCell(builder, FormatCost(metrics.TotalActualCost));
                builder.Append("</tr>");
            }
        }

        builder.Append("</tbody></table></section>");
    }

    private static void AppendMetricCell(StringBuilder builder, string label, string value)
    {
        builder.Append("<td width=\"33.33%\" style=\"padding:12px;vertical-align:top;\">")
            .Append("<span style=\"display:block;color:#64748b;font-size:11px;\">")
            .Append(Escape(label)).Append("</span><strong style=\"display:block;margin-top:4px;")
            .Append("font-size:15px;color:#0f172a;\">").Append(Escape(value)).Append("</strong></td>");
    }

    private static void AppendNumericCell(StringBuilder builder, string value)
    {
        builder.Append("<td style=\"padding:9px 10px;text-align:right;border-bottom:1px solid #f1f5f9;")
            .Append("font-variant-numeric:tabular-nums;white-space:nowrap;\">")
            .Append(Escape(value)).Append("</td>");
    }

    private static ReportUsageMetrics GetMetrics(
        IReadOnlyList<ReportWindowMetrics> windows,
        string windowKey) => windows
            .FirstOrDefault(item => string.Equals(item.WindowKey, windowKey, StringComparison.Ordinal))
            ?.Metrics
            ?? EmptyMetrics;

    private static readonly ReportUsageMetrics EmptyMetrics = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0m,
        0m,
        0m);

    private static (DateOnly Start, DateOnly End) GetDateRange(ReportDocument report)
    {
        var fallbackDate = DateOnly.FromDateTime(report.GeneratedAt.UtcDateTime);
        return report.Windows.Count == 0
            ? (fallbackDate, fallbackDate)
            : (
                report.Windows.Min(window => window.StartDate),
                report.Windows.Max(window => window.EndDateExclusive).AddDays(-1));
    }

    private static string BuildStatusLabel(ReportDocument report) =>
        report.Status == ReportStatus.Partial ? "部分完成" : "完整报告";

    private static string BuildFooter(ReportDocument report) =>
        $"报告编号 {report.ReportId:D}｜生成时间 "
        + $"{report.GeneratedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC";

    private static string GetWindowLabel(ReportWindowDescriptor window) =>
        ReportWindows.GetDisplayLabel(window.Kind, window.Label);

    private static string FormatWindowRange(ReportWindowDescriptor window) =>
        $"{window.StartDate:yyyy-MM-dd} 至 {window.EndDateExclusive.AddDays(-1):yyyy-MM-dd}，共 {window.DayCount} 天";

    private static string FormatCount(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatCost(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        global::System.Net.WebUtility.HtmlEncode(value);

    private static string EscapeMarkdown(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#'
                or '+' or '-' or '.' or '!' or '>' or '|')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
