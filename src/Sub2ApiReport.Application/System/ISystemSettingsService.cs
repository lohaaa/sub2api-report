namespace Sub2ApiReport.Application.System;

public interface ISystemSettingsService
{
    Task<SystemSettingsSnapshot> GetAsync(CancellationToken cancellationToken);

    Task<SystemSettingsSnapshot> UpdateAsync(
        UpdateSystemSettingsCommand command,
        CancellationToken cancellationToken);
}

public sealed record SystemSettingsSnapshot(
    string Timezone,
    string ReleaseChannel,
    string LogLevel,
    int ReportRetentionMonths,
    int BackupRetentionCount,
    long Revision,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateSystemSettingsCommand(
    string Timezone,
    string ReleaseChannel,
    string LogLevel,
    int ReportRetentionMonths,
    int BackupRetentionCount,
    long ExpectedRevision);

public sealed class SystemSettingsConflictException(long expectedRevision, long actualRevision)
    : Exception($"System settings revision {expectedRevision} is stale; current revision is {actualRevision}.")
{
    public long ExpectedRevision { get; } = expectedRevision;

    public long ActualRevision { get; } = actualRevision;
}
