using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;
using Sub2ApiReport.Infrastructure.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal sealed class DatabaseReportDeliveryService(
    ReportDbContext dbContext,
    ChannelSecretProtector protector,
    IEnumerable<IReportSender> senders,
    TimeProvider timeProvider) : IReportDeliveryService
{
    private readonly Dictionary<NotificationChannelType, IReportSender> _senders =
        senders.ToDictionary(sender => sender.ChannelType);

    public async Task<DeliveryRunDocument> DeliverAsync(
        DeliverReportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ChannelIds.Count == 0)
        {
            throw new ReportDeliveryPreconditionException("Select at least one delivery channel.");
        }

        var report = await LoadReportAsync(command.ReportId, cancellationToken);
        if (report.Status == ReportStatus.Partial && !command.ConfirmPartial)
        {
            throw new ReportDeliveryPreconditionException(
                "The report is partial; delivering it requires an explicit confirmation.");
        }

        var channels = await LoadChannelsAsync(command.ChannelIds.Distinct().ToArray(), cancellationToken);
        var run = ReportRun.StartManual(report.ReportId, timeProvider.GetUtcNow());
        var work = new List<DeliveryWork>();
        foreach (var channel in channels)
        {
            var context = ChannelRuntimeMapper.CreateContext(channel, protector);
            var sender = ResolveSender(channel.Type);
            var parts = sender.Render(report, context);
            var aggregateHash = ComputeAggregateHash(parts);
            var delivery = DeliveryRecord.Create(
                channel.Id,
                channel.Type,
                channel.Name,
                aggregateHash,
                parts.Select(part => DeliveryPart.Create(part.Index, part.Count, part.PayloadHash)).ToArray());
            run.Deliveries.Add(delivery);
            work.Add(new DeliveryWork(delivery, context, sender, parts));
        }

        dbContext.ReportRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SendAsync(run, work, cancellationToken);
        return Map(run);
    }

    public async Task<DeliveryRunDocument> RetryAsync(
        RetryDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ReportRuns
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .SingleOrDefaultAsync(
                item => item.Id == command.RunId && item.ReportSnapshotId == command.ReportId,
                cancellationToken)
            ?? throw new ReportRunNotFoundException(command.ReportId, command.RunId);
        if (!run.IsRetryable)
        {
            throw new ReportRunNotRetryableException(run.Id);
        }

        var report = await LoadReportAsync(run.ReportSnapshotId, cancellationToken);
        var work = new List<DeliveryWork>();
        foreach (var delivery in run.Deliveries
            .Where(delivery => delivery.Status == DeliveryStatus.Failed)
            .OrderBy(delivery => delivery.ChannelName, StringComparer.Ordinal))
        {
            var channel = await dbContext.NotificationChannels
                .SingleOrDefaultAsync(item => item.Id == delivery.ChannelId, cancellationToken);
            if (channel is not { Enabled: true })
            {
                continue;
            }

            var context = ChannelRuntimeMapper.CreateContext(channel, protector);
            var sender = ResolveSender(channel.Type);
            var parts = sender.Render(report, context);
            var aggregateHash = ComputeAggregateHash(parts);
            var existingParts = delivery.Parts.OrderBy(part => part.PartIndex).ToArray();
            for (var index = 0; index < existingParts.Length; index++)
            {
                var part = existingParts[index];
                if (index < parts.Count)
                {
                    part.RebindForRetry(parts[index].PayloadHash, parts.Count);
                    continue;
                }

                delivery.Parts.Remove(part);
            }

            for (var index = existingParts.Length; index < parts.Count; index++)
            {
                delivery.Parts.Add(DeliveryPart.Create(index, parts.Count, parts[index].PayloadHash));
            }

            delivery.ResetForRetry(aggregateHash);
            work.Add(new DeliveryWork(delivery, context, sender, parts));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SendAsync(run, work, cancellationToken);
        return Map(run);
    }

    public async Task<IReadOnlyList<DeliveryRunDocument>> GetRunsAsync(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.ReportRuns
            .AsNoTracking()
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .Where(item => item.ReportSnapshotId == reportId)
            .OrderByDescending(item => item.StartedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return runs.Select(Map).ToArray();
    }

    private async Task SendAsync(
        ReportRun run,
        IReadOnlyList<DeliveryWork> work,
        CancellationToken cancellationToken)
    {
        var wasCancelled = false;
        foreach (var item in work)
        {
            if (wasCancelled)
            {
                continue;
            }

            try
            {
                item.Delivery.MarkSending();
                await dbContext.SaveChangesAsync(cancellationToken);
                ChannelSendOutcome? failure = null;
                foreach (var (part, rendered) in item.Delivery.Parts
                    .OrderBy(part => part.PartIndex)
                    .Zip(item.RenderedParts))
                {
                    var outcome = await item.Sender.SendPartAsync(
                        rendered,
                        item.Context,
                        cancellationToken);
                    if (outcome.Succeeded)
                    {
                        part.MarkSucceeded(timeProvider.GetUtcNow());
                        continue;
                    }

                    part.MarkFailed(outcome.ErrorCode ?? "failed", outcome.ErrorMessage);
                    failure = outcome;
                    break;
                }

                if (failure is { } failedOutcome)
                {
                    item.Delivery.MarkFailed(
                        failedOutcome.ErrorCode ?? "failed",
                        failedOutcome.ErrorMessage);
                }
                else
                {
                    item.Delivery.MarkSucceeded(timeProvider.GetUtcNow());
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                item.Delivery.MarkFailed("cancelled", null);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                wasCancelled = true;
            }
            catch (Exception)
            {
                item.Delivery.MarkFailed("internal_error", null);
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }

        var status = ComputeRunStatus(run.Deliveries);
        if (run.Status == ReportRunStatus.Running)
        {
            run.Complete(status, timeProvider.GetUtcNow());
        }
        else
        {
            run.RecordRetryResult(status, timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private IReportSender ResolveSender(NotificationChannelType type)
    {
        var sender = _senders.TryGetValue(type, out var resolved)
            ? resolved
            : throw new InvalidOperationException(
                $"No report sender is registered for channel type {type}.");
        return sender;
    }

    private async Task<ReportDocument> LoadReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var canonicalJson = await dbContext.ReportSnapshots
            .AsNoTracking()
            .Where(item => item.Id == reportId)
            .Select(item => item.CanonicalJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ReportNotFoundException(reportId);
        return ReportCanonicalSerializer.Deserialize(canonicalJson)
            ?? throw new ReportNotFoundException(reportId);
    }

    private async Task<List<NotificationChannel>> LoadChannelsAsync(
        IReadOnlyList<Guid> channelIds,
        CancellationToken cancellationToken)
    {
        var channels = await dbContext.NotificationChannels
            .Where(channel => channelIds.Contains(channel.Id))
            .ToListAsync(cancellationToken);
        var missing = channelIds.Except(channels.Select(channel => channel.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ReportDeliveryPreconditionException(
                "One or more selected channels do not exist.");
        }

        if (channels.Any(channel => !channel.Enabled))
        {
            throw new ReportDeliveryPreconditionException(
                "One or more selected channels are disabled.");
        }

        return channels
            .OrderBy(channel => channel.CreatedAt)
            .ThenBy(channel => channel.Id)
            .ToList();
    }

    private static string ComputeAggregateHash(IReadOnlyList<OutboundPart> parts) =>
        DeliveryPayloadHash.Compute(
            parts[0].Subject,
            string.Join("\n\n", parts.Select(part => part.Body)),
            parts[0].CsvContent);

    private static ReportRunStatus ComputeRunStatus(IReadOnlyList<DeliveryRecord> deliveries)
    {
        var statuses = deliveries.Select(delivery => delivery.Status).ToArray();
        return statuses.Length == 0
            ? ReportRunStatus.Failed
            : statuses.All(status => status == DeliveryStatus.Succeeded)
                ? ReportRunStatus.Succeeded
                : statuses.All(status => status == DeliveryStatus.Failed)
                    ? ReportRunStatus.Failed
                    : ReportRunStatus.PartialFailed;
    }

    private static DeliveryRunDocument Map(ReportRun run) => new(
        run.Id,
        run.ReportSnapshotId,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.Deliveries
            .OrderBy(delivery => delivery.ChannelName, StringComparer.Ordinal)
            .Select(delivery => new DeliveryDocument(
                delivery.Id,
                delivery.ChannelId,
                delivery.ChannelType.ToString(),
                delivery.ChannelName,
                delivery.Status,
                delivery.Attempts,
                delivery.ErrorCode,
                delivery.ErrorMessage,
                delivery.SentAt,
                delivery.Parts
                    .OrderBy(part => part.PartIndex)
                    .Select(part => new DeliveryPartDocument(
                        part.PartIndex,
                        part.PartCount,
                        part.Status,
                        part.Attempts,
                        part.ErrorCode,
                        part.SentAt))
                    .ToArray()))
            .ToArray());

    private sealed record DeliveryWork(
        DeliveryRecord Delivery,
        ChannelDeliveryContext Context,
        IReportSender Sender,
        IReadOnlyList<OutboundPart> RenderedParts);
}
