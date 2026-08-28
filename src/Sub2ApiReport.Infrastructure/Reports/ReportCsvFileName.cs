using System.Globalization;
using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.Infrastructure.Reports;

internal static class ReportCsvFileName
{
    public static string Create(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var reportDate = report.Windows.Count == 0
            ? DateOnly.FromDateTime(report.GeneratedAt.UtcDateTime)
            : report.Windows.Max(window => window.EndDateExclusive).AddDays(-1);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"sub2api-report-{reportDate:yyyy-MM-dd}.csv");
    }
}
