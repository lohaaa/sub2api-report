using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.System;

internal sealed class DatabaseSystemSettingsService(
    ReportDbContext dbContext,
    TimeProvider timeProvider) : ISystemSettingsService
{
    public async Task<SystemSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.Id == Domain.System.SystemSetting.SingletonId, cancellationToken);

        return Map(setting);
    }

    public async Task<SystemSettingsSnapshot> UpdateAsync(
        UpdateSystemSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTimezone(command.Timezone);

        var setting = await dbContext.SystemSettings
            .SingleAsync(setting => setting.Id == Domain.System.SystemSetting.SingletonId, cancellationToken);

        if (setting.Revision != command.ExpectedRevision)
        {
            throw new SystemSettingsConflictException(command.ExpectedRevision, setting.Revision);
        }

        setting.Update(
            command.Timezone,
            command.ReleaseChannel,
            command.LogLevel,
            command.ReportConcurrency,
            command.ReportRetentionMonths,
            command.BackupRetentionCount,
            timeProvider.GetUtcNow());

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SystemSettingsConflictException(
                command.ExpectedRevision,
                command.ExpectedRevision + 1);
        }

        return Map(setting);
    }

    private static SystemSettingsSnapshot Map(Domain.System.SystemSetting setting) => new(
        setting.Timezone,
        setting.ReleaseChannel,
        setting.LogLevel,
        setting.ReportConcurrency,
        setting.ReportRetentionMonths,
        setting.BackupRetentionCount,
        setting.Revision,
        setting.UpdatedAt);

    private static void ValidateTimezone(string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("Unknown time zone.", nameof(timezone), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Invalid time zone data.", nameof(timezone), exception);
        }
    }
}
