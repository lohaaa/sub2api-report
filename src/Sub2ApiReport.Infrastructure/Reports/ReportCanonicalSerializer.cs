using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Reports;

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

    public static LegacyReportDocument DeserializeLegacy(string canonicalJson) =>
        JsonSerializer.Deserialize<LegacyReportDocument>(canonicalJson, Options)
        ?? throw new InvalidOperationException("The stored report snapshot is invalid.");
}

/// <summary>Canonical shape of schema v1/v2 snapshots; kept only for reading immutable history.</summary>
public sealed record LegacyReportDocument(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    ReportWindow SevenDayWindow,
    ReportWindow ThirtyDayWindow,
    ReportUsageMetrics SevenDayTotal,
    ReportUsageMetrics ThirtyDayTotal,
    IReadOnlyList<LegacyReportPerson> People,
    IReadOnlyList<LegacyReportKey> Keys,
    LegacyReportDiagnostics? Diagnostics);

public sealed record LegacyReportPerson(
    Guid PersonId,
    string Code,
    string DisplayName,
    int KeyCount,
    ReportUsageMetrics SevenDay,
    ReportUsageMetrics ThirtyDay);

public sealed record LegacyReportKey(
    Guid KeyId,
    string ExternalId,
    long? SourceUserId,
    string? SourceUserEmail,
    string Name,
    string Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RetiredAt,
    ReportUsageMetrics SevenDay,
    ReportUsageMetrics ThirtyDay);

public sealed record LegacyReportDiagnostics(
    IReadOnlyList<LegacyReportSegmentDiagnostic> FailedSegments,
    IReadOnlyList<LegacyReportSegmentDiagnostic>? UnassignedSegments,
    IReadOnlyList<LegacyReportSegmentDiagnostic>? ConflictingSegments,
    IReadOnlyList<string>? ZeroUsageKeyIds);

public sealed record LegacyReportSegmentDiagnostic(
    string ExternalKeyId,
    string KeyName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Code,
    Sub2ApiFailureKind? FailureKind);

public static class LegacyReportDocumentMapper
{
    public static ReportDocument MapFromLegacy(LegacyReportDocument legacy)
    {
        var users = legacy.People
            .Select(person => new ReportUserUsage(
                person.PersonId,
                0,
                null,
                person.DisplayName,
                person.KeyCount,
                person.SevenDay,
                person.ThirtyDay))
            .ToArray();
        var keys = legacy.Keys
            .Select(key => new ReportKeyUsage(
                key.KeyId,
                key.ExternalId,
                key.SourceUserId,
                key.SourceUserEmail,
                key.Name,
                key.Status,
                key.LastUsedAt,
                key.RetiredAt,
                key.SevenDay,
                key.ThirtyDay))
            .ToArray();
        var failedRanges = (legacy.Diagnostics?.FailedSegments ?? [])
            .Select(failure => new ReportRangeFailure(
                0,
                string.Empty,
                long.TryParse(failure.ExternalKeyId, out var externalId)
                    ? externalId
                    : 0,
                failure.KeyName,
                failure.StartDate,
                failure.EndDate,
                failure.FailureKind,
                failure.Code))
            .ToArray();
        return new ReportDocument(
            legacy.SchemaVersion,
            legacy.ReportId,
            legacy.Status,
            legacy.Trigger,
            legacy.GeneratedAt,
            legacy.Timezone,
            legacy.ConnectionRevision,
            legacy.SevenDayWindow,
            legacy.ThirtyDayWindow,
            legacy.SevenDayTotal,
            legacy.ThirtyDayTotal,
            users,
            keys,
            new ReportDiagnostics(failedRanges));
    }
}
