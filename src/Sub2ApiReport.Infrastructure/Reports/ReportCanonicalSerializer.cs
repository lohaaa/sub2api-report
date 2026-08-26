using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Reports;

namespace Sub2ApiReport.Infrastructure.Reports;

internal static class ReportCanonicalSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ReportDocument report) => JsonSerializer.Serialize(report, Options);

    public static ReportDocument Deserialize(string canonicalJson) =>
        JsonSerializer.Deserialize<ReportDocument>(canonicalJson, Options)
        ?? throw new InvalidOperationException("The stored report snapshot is invalid.");
}
