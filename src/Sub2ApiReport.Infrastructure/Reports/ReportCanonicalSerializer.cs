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

    /// <summary>Deserializes a canonical snapshot by its explicit stored schema version.</summary>
    public static ReportDocument Deserialize(string canonicalJson, int schemaVersion) => schemaVersion switch
    {
        1 or 2 => LegacyReportDocumentMapper.MapFromLegacy(DeserializeLegacy(canonicalJson)),
        3 => LegacyV3ReportDocumentMapper.MapFromLegacyV3(DeserializeLegacyV3(canonicalJson)),
        ReportSnapshot.CurrentSchemaVersion => DeserializeCurrent(canonicalJson),
        _ => throw new InvalidOperationException(
            $"The stored report snapshot schema version {schemaVersion} is not supported."),
    };

    public static ReportDocument DeserializeCurrent(string canonicalJson) =>
        JsonSerializer.Deserialize<ReportDocument>(canonicalJson, Options)
        ?? throw new InvalidOperationException("The stored report snapshot is invalid.");

    public static LegacyReportDocument DeserializeLegacy(string canonicalJson) =>
        JsonSerializer.Deserialize<LegacyReportDocument>(canonicalJson, Options)
        ?? throw new InvalidOperationException("The stored report snapshot is invalid.");

    public static LegacyV3ReportDocument DeserializeLegacyV3(string canonicalJson) =>
        JsonSerializer.Deserialize<LegacyV3ReportDocument>(canonicalJson, Options)
        ?? throw new InvalidOperationException("The stored report snapshot is invalid.");
}

public sealed record LegacyReportWindow(int Days, DateOnly StartDate, DateOnly EndDate);

/// <summary>Canonical shape of schema v1/v2 snapshots; kept only for reading immutable history.</summary>
public sealed record LegacyReportDocument(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    LegacyReportWindow SevenDayWindow,
    LegacyReportWindow ThirtyDayWindow,
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

/// <summary>Canonical shape of schema v3 snapshots; kept only for reading immutable history.</summary>
public sealed record LegacyV3ReportDocument(
    int SchemaVersion,
    Guid ReportId,
    ReportStatus Status,
    ReportTrigger Trigger,
    DateTimeOffset GeneratedAt,
    string Timezone,
    long ConnectionRevision,
    LegacyV3ReportWindow SevenDayWindow,
    LegacyV3ReportWindow ThirtyDayWindow,
    ReportUsageMetrics SevenDayTotal,
    ReportUsageMetrics ThirtyDayTotal,
    IReadOnlyList<LegacyV3ReportUserUsage> Users,
    IReadOnlyList<LegacyV3ReportKeyUsage> Keys,
    LegacyV3ReportDiagnostics? Diagnostics);

public sealed record LegacyV3ReportWindow(int Days, DateOnly StartDate, DateOnly EndDate);

public sealed record LegacyV3ReportUserUsage(
    Guid UserId,
    long ExternalUserId,
    string? Username,
    string Email,
    int KeyCount,
    ReportUsageMetrics SevenDay,
    ReportUsageMetrics ThirtyDay);

public sealed record LegacyV3ReportKeyUsage(
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

public sealed record LegacyV3ReportDiagnostics(
    IReadOnlyList<LegacyV3ReportRangeFailure> FailedRanges);

public sealed record LegacyV3ReportRangeFailure(
    long ExternalUserId,
    string UserEmail,
    long ExternalKeyId,
    string KeyName,
    DateOnly StartDate,
    DateOnly EndDate,
    Sub2ApiFailureKind? FailureKind,
    string? ErrorCode);

public static class LegacyReportDocumentMapper
{
    public static ReportDocument MapFromLegacy(LegacyReportDocument legacy)
    {
        var windows = CreateLegacyWindows(legacy.SevenDayWindow, legacy.ThirtyDayWindow);
        var windowTotals = new ReportWindowMetrics[]
        {
            new(ReportWindows.RollingSevenDaysKey, legacy.SevenDayTotal),
            new(ReportWindows.RollingThirtyDaysKey, legacy.ThirtyDayTotal),
        };
        var users = legacy.People
            .Select(person => new ReportUserUsage(
                person.PersonId,
                0,
                null,
                person.DisplayName,
                person.KeyCount,
                CreateWindowMetrics(person.SevenDay, person.ThirtyDay)))
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
                CreateWindowMetrics(key.SevenDay, key.ThirtyDay)))
            .ToArray();
        var failedRanges = (legacy.Diagnostics?.FailedSegments ?? [])
            .Select(failure => new ReportRangeFailure(
                0,
                string.Empty,
                long.TryParse(failure.ExternalKeyId, out var externalId)
                    ? externalId
                    : 0,
                failure.KeyName,
                ResolveLegacyFailureWindowKey(
                    failure.StartDate,
                    failure.EndDate,
                    legacy.SevenDayWindow.StartDate,
                    legacy.SevenDayWindow.EndDate),
                failure.StartDate,
                failure.EndDate.AddDays(1),
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
            windows,
            windowTotals,
            users,
            keys,
            new ReportDiagnostics(failedRanges));
    }

    internal static ReportDocument MapFromLegacyV3(LegacyV3ReportDocument legacy)
    {
        var windows = CreateLegacyWindows(
            new LegacyReportWindow(
                legacy.SevenDayWindow.Days,
                legacy.SevenDayWindow.StartDate,
                legacy.SevenDayWindow.EndDate),
            new LegacyReportWindow(
                legacy.ThirtyDayWindow.Days,
                legacy.ThirtyDayWindow.StartDate,
                legacy.ThirtyDayWindow.EndDate));
        var windowTotals = new ReportWindowMetrics[]
        {
            new(ReportWindows.RollingSevenDaysKey, legacy.SevenDayTotal),
            new(ReportWindows.RollingThirtyDaysKey, legacy.ThirtyDayTotal),
        };
        var users = legacy.Users
            .Select(user => new ReportUserUsage(
                user.UserId,
                user.ExternalUserId,
                user.Username,
                user.Email,
                user.KeyCount,
                CreateWindowMetrics(user.SevenDay, user.ThirtyDay)))
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
                CreateWindowMetrics(key.SevenDay, key.ThirtyDay)))
            .ToArray();
        var failedRanges = (legacy.Diagnostics?.FailedRanges ?? [])
            .Select(failure => new ReportRangeFailure(
                failure.ExternalUserId,
                failure.UserEmail,
                failure.ExternalKeyId,
                failure.KeyName,
                ResolveLegacyFailureWindowKey(
                    failure.StartDate,
                    failure.EndDate,
                    legacy.SevenDayWindow.StartDate,
                    legacy.SevenDayWindow.EndDate),
                failure.StartDate,
                failure.EndDate.AddDays(1),
                failure.FailureKind,
                failure.ErrorCode))
            .ToArray();
        return new ReportDocument(
            legacy.SchemaVersion,
            legacy.ReportId,
            legacy.Status,
            legacy.Trigger,
            legacy.GeneratedAt,
            legacy.Timezone,
            legacy.ConnectionRevision,
            windows,
            windowTotals,
            users,
            keys,
            new ReportDiagnostics(failedRanges));
    }

    private static string ResolveLegacyFailureWindowKey(
        DateOnly failureStartDate,
        DateOnly failureEndDate,
        DateOnly sevenDayStartDate,
        DateOnly sevenDayEndDate) =>
        failureStartDate == sevenDayStartDate && failureEndDate == sevenDayEndDate
            ? ReportWindows.RollingSevenDaysKey
            : ReportWindows.RollingThirtyDaysKey;

    private static ReportWindowDescriptor[] CreateLegacyWindows(
        LegacyReportWindow sevenDay,
        LegacyReportWindow thirtyDay) =>
    [
        new(
            ReportWindows.RollingSevenDaysKey,
            ReportWindowKind.RollingDays,
            sevenDay.Days,
            null,
            sevenDay.StartDate,
            sevenDay.EndDate.AddDays(1),
            sevenDay.Days,
            "最近 7 天"),
        new(
            ReportWindows.RollingThirtyDaysKey,
            ReportWindowKind.RollingDays,
            thirtyDay.Days,
            null,
            thirtyDay.StartDate,
            thirtyDay.EndDate.AddDays(1),
            thirtyDay.Days,
            "最近 30 天"),
    ];

    private static ReportWindowMetrics[] CreateWindowMetrics(
        ReportUsageMetrics sevenDay,
        ReportUsageMetrics thirtyDay) =>
    [
        new(ReportWindows.RollingSevenDaysKey, sevenDay),
        new(ReportWindows.RollingThirtyDaysKey, thirtyDay),
    ];
}

public static class LegacyV3ReportDocumentMapper
{
    public static ReportDocument MapFromLegacyV3(LegacyV3ReportDocument legacy) =>
        LegacyReportDocumentMapper.MapFromLegacyV3(legacy);
}
