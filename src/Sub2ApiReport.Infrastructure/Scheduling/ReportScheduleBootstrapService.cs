using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sub2ApiReport.Application.Scheduling;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Scheduling;

internal sealed class ReportScheduleBootstrapService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ReportScheduleBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IReportScheduleCoordinator>();
            await RecoverInterruptedManualWorkAsync(dbContext, cancellationToken);
            var schedule = await dbContext.ReportSchedules
                .AsNoTracking()
                .SingleAsync(item => item.Id == ReportSchedule.SingletonId, cancellationToken);
            var projection = await coordinator.ApplyAsync(
                new ReportScheduleSnapshot(
                    schedule.Id,
                    schedule.Enabled,
                    schedule.DayOfMonth,
                    schedule.LocalTime,
                    schedule.Timezone,
                    schedule.WindowSpecsJson,
                    schedule.Revision,
                    schedule.UpdatedAt),
                cancellationToken);
            if (!projection.Synchronized)
            {
                ScheduleBootstrapLog.Failed(logger, projection.ErrorCode ?? "unknown");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ScheduleBootstrapLog.Crashed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverInterruptedManualWorkAsync(
        ReportDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var generationRuns = await dbContext.ReportGenerationRuns
            .Where(run => run.Status == ReportGenerationStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var generationRun in generationRuns)
        {
            generationRun.MarkFailed(
                "interrupted",
                "interrupted",
                "报告生成在应用重启前未完成。",
                generationRun.ConnectionRevision,
                now);
        }

        var manualRuns = await dbContext.ReportRuns
            .Include(run => run.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .AsSplitQuery()
            .Where(run => run.Trigger == ReportRunTrigger.ManualDelivery
                && run.Status == ReportRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var run in manualRuns)
        {
            foreach (var delivery in run.Deliveries)
            {
                if (delivery.Status == DeliveryStatus.Pending)
                {
                    delivery.MarkSending();
                    foreach (var part in delivery.Parts.Where(part =>
                        part.Status == DeliveryPartStatus.Pending))
                    {
                        part.MarkFailed("interrupted_before_send", null);
                    }

                    delivery.MarkFailed("interrupted_before_send", null);
                }
                else if (delivery.Status == DeliveryStatus.Sending)
                {
                    foreach (var part in delivery.Parts.Where(part =>
                        part.Status == DeliveryPartStatus.Pending))
                    {
                        part.MarkFailed("outcome_unknown", null);
                    }

                    delivery.MarkFailed("outcome_unknown", null);
                }
            }

            run.Fail("interrupted", "手工投递在应用重启前未完成。", now);
        }

        if (generationRuns.Count > 0 || manualRuns.Count > 0)
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
            ScheduleBootstrapLog.Recovered(
                logger,
                generationRuns.Count,
                manualRuns.Count);
        }
    }
}

internal static partial class ScheduleBootstrapLog
{
    [LoggerMessage(
        EventId = 53,
        Level = LogLevel.Error,
        Message = "Report schedule bootstrap failed with code {ErrorCode}")]
    public static partial void Failed(ILogger logger, string errorCode);

    [LoggerMessage(
        EventId = 54,
        Level = LogLevel.Error,
        Message = "Report schedule bootstrap crashed")]
    public static partial void Crashed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 55,
        Level = LogLevel.Warning,
        Message = "Recovered {GenerationRunCount} interrupted report generations and {ManualRunCount} manual deliveries")]
    public static partial void Recovered(
        ILogger logger,
        int generationRunCount,
        int manualRunCount);
}
