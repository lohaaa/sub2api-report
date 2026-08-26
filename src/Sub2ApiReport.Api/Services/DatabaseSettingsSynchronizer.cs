using Serilog.Core;
using Serilog.Events;
using Sub2ApiReport.Application.System;

namespace Sub2ApiReport.Api.Services;

internal sealed class DatabaseSettingsSynchronizer(
    IServiceScopeFactory scopeFactory,
    LoggingLevelSwitch loggingLevelSwitch,
    ILogger<DatabaseSettingsSynchronizer> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private long _appliedRevision;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ApplyAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ApplyAsync(stoppingToken);
        }
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
            var settings = await settingsService.GetAsync(cancellationToken);

            if (settings.Revision == _appliedRevision)
            {
                return;
            }

            if (!Enum.TryParse<LogEventLevel>(settings.LogLevel, ignoreCase: true, out var level))
            {
                DatabaseSettingsLog.UnsupportedLogLevel(
                    logger,
                    settings.Revision,
                    settings.LogLevel);
                return;
            }

            loggingLevelSwitch.MinimumLevel = level;
            _appliedRevision = settings.Revision;
            DatabaseSettingsLog.Applied(logger, settings.Revision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DatabaseSettingsLog.RefreshFailed(logger, exception);
        }
    }
}

internal static partial class DatabaseSettingsLog
{
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "Database settings revision {Revision} contains unsupported log level {LogLevel}")]
    public static partial void UnsupportedLogLevel(ILogger logger, long revision, string logLevel);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Applied database settings revision {Revision}")]
    public static partial void Applied(ILogger logger, long revision);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Unable to refresh dynamic database settings")]
    public static partial void RefreshFailed(ILogger logger, Exception exception);
}
