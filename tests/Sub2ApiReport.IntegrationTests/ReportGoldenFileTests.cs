using System.Text;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;
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

        Assert.Equal(ReadGolden("report-v3.json").TrimEnd(), json);
        Assert.True(csv.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var csvText = Encoding.UTF8.GetString(csv[Encoding.UTF8.GetPreamble().Length..])
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(ReadGolden("report-v3.csv").Replace("\r\n", "\n", StringComparison.Ordinal), csvText);
    }

    private static ReportDocument CreateReport()
    {
        var sevenDay = Metrics(7, 700, 1.25m, 0.75m);
        var thirtyDay = Metrics(30, 9007199254740993, 4.5m, 3.25m);
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var keyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        return new ReportDocument(
            3,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReportStatus.Complete,
            ReportTrigger.ManualDryRun,
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "Asia/Shanghai",
            3,
            new ReportWindow(7, new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 25)),
            new ReportWindow(30, new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 25)),
            sevenDay,
            thirtyDay,
            [new ReportUserUsage(userId, 42, "synthetic-user", "=Synthetic User", 1, sevenDay, thirtyDay)],
            [new ReportKeyUsage(
                keyId,
                "9007199254740993",
                42,
                "=Synthetic User",
                "Synthetic Key",
                "active",
                null,
                null,
                sevenDay,
                thirtyDay)],
            new ReportDiagnostics([new ReportRangeFailure(
                42,
                "=Synthetic User",
                9007199254740993,
                "Synthetic Key",
                new DateOnly(2026, 7, 27),
                new DateOnly(2026, 8, 25),
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
