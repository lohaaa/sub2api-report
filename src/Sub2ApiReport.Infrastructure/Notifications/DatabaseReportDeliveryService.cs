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
    IReportDownloadService reportDownloadService,
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
            var deliveryId = Guid.NewGuid();
            var context = await CreateContextAsync(
                channel,
                report.ReportId,
                deliveryId,
                cancellationToken);
            var sender = ResolveSender(channel.Type);
            var parts = sender.Render(report, context);
            var aggregateHash = ComputeAggregateHash(parts);
            var delivery = DeliveryRecord.Create(
                channel.Id,
                channel.Type,
                channel.Name,
                aggregateHash,
                parts.Select(part => DeliveryPart.Create(part.Index, part.Count, part.PayloadHash)).ToArray(),
                deliveryId);
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
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.DownloadGrant)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                item => item.Id == command.RunId && item.ReportSnapshotId == command.ReportId,
                cancellationToken)
            ?? throw new ReportRunNotFoundException(command.ReportId, command.RunId);
        if (!run.IsRetryable
            || !run.Deliveries.Any(delivery => delivery.Status == DeliveryStatus.Failed))
        {
            throw new ReportRunNotRetryableException(run.Id);
        }

        if (run.Deliveries.Any(delivery =>
            delivery.Status == DeliveryStatus.Failed
            && delivery.ErrorCode == "outcome_unknown"))
        {
            throw new ReportDeliveryPreconditionException(
                "Deliveries with an unknown outcome require an explicit task retry confirmation.");
        }

        var report = await LoadReportAsync(
            run.ReportSnapshotId
                ?? throw new ReportNotFoundException(command.ReportId),
            cancellationToken);
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

            var context = await CreateContextAsync(
                channel,
                report.ReportId,
                delivery.Id,
                cancellationToken);
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
                // 新分片同样带显式外键并经 DbSet.Add 追踪，避免依赖集合导航图解析出的错误状态。
                var deliveryPart = DeliveryPart.Create(
                    index,
                    parts.Count,
                    parts[index].PayloadHash,
                    delivery.Id);
                dbContext.DeliveryParts.Add(deliveryPart);
                delivery.Parts.Add(deliveryPart);
            }

            delivery.ResetForRetry(aggregateHash);
            work.Add(new DeliveryWork(delivery, context, sender, parts));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SendAsync(run, work, cancellationToken);
        return Map(run);
    }

    public async Task<DeliveryRunDocument> DeliverTaskAsync(
        DeliverReportTaskCommand command,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ReportRuns
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.Parts)
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.DownloadGrant)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == command.RunId, cancellationToken)
            ?? throw new ReportRunNotFoundException(Guid.Empty, command.RunId);
        if (run.ReportSnapshotId is null)
        {
            throw new ReportDeliveryPreconditionException(
                "The report task has no snapshot to deliver.");
        }

        var report = await LoadReportAsync(run.ReportSnapshotId.Value, cancellationToken);
        var work = new List<DeliveryWork>();
        if (run.Status == ReportRunStatus.Delivering)
        {
            if (!command.Recovering)
            {
                throw new ReportDeliveryPreconditionException(
                    "The report task is already delivering.");
            }

            foreach (var delivery in run.Deliveries.Where(item => item.Status == DeliveryStatus.Sending))
            {
                foreach (var part in delivery.Parts.Where(item => item.Status == DeliveryPartStatus.Pending))
                {
                    part.MarkFailed("outcome_unknown", null);
                }

                delivery.MarkFailed("outcome_unknown", null);
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
            await PreparePendingTaskWorkAsync(run, report, work, cancellationToken);
        }
        else if (run.Status is ReportRunStatus.Queued or ReportRunStatus.Rendering)
        {
            var channelIds = await ResolveTaskChannelIdsAsync(run, cancellationToken);
            var channels = await LoadChannelsAsync(channelIds, cancellationToken);
            foreach (var channel in channels)
            {
                var deliveryId = Guid.NewGuid();
                var context = await CreateContextAsync(
                    channel,
                    report.ReportId,
                    deliveryId,
                    cancellationToken);
                var sender = ResolveSender(channel.Type);
                var parts = sender.Render(report, context);
                var delivery = DeliveryRecord.Create(
                    channel.Id,
                    channel.Type,
                    channel.Name,
                    ComputeAggregateHash(parts),
                    parts.Select(part => DeliveryPart.Create(part.Index, part.Count, part.PayloadHash, deliveryId)).ToArray(),
                    deliveryId,
                    run.Id);
                // 通过 DbSet.Add 与显式主外键把整个投递图记录为新增实体。禁止把新投递挂到
                // 已跟踪（非 Added）run 的集合导航上：在 EF Core 10 的关系图中，这样解析出的
                // DeliveryRecord/DeliveryPart 会被标记为 Modified，保存时执行 UPDATE 0 行并
                // 抛出 DbUpdateConcurrencyException，导致任务永久停滞在快照阶段。
                dbContext.DeliveryRecords.Add(delivery);
                work.Add(new DeliveryWork(delivery, context, sender, parts));
            }

            run.BeginDelivering(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            throw new ReportDeliveryPreconditionException(
                "The report task is not ready for delivery.");
        }

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
            .Include(item => item.Deliveries)
            .ThenInclude(delivery => delivery.DownloadGrant)
            .AsSplitQuery()
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
                    if (item.Context.ReportDownloadUrl is not null)
                    {
                        await reportDownloadService.ActivateAsync(
                            item.Delivery.Id,
                            cancellationToken);
                    }

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
        if (run.Status is ReportRunStatus.Running or ReportRunStatus.Delivering)
        {
            run.Complete(status, timeProvider.GetUtcNow());
        }
        else
        {
            run.RecordRetryResult(status, timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<ChannelDeliveryContext> CreateContextAsync(
        NotificationChannel channel,
        Guid reportId,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        var context = ChannelRuntimeMapper.CreateContext(channel, protector);
        if (channel.Type == NotificationChannelType.Email)
        {
            return context;
        }

        var link = await reportDownloadService.PrepareLinkAsync(
            reportId,
            deliveryId,
            cancellationToken);
        return context with
        {
            ReportDownloadUrl = link?.Url,
            ReportDownloadPolicy = link is null ? null : FormatDownloadPolicy(link),
        };
    }


    private static string FormatDownloadPolicy(ReportDownloadLink link)
    {
        var lifetime = link.LifetimeHours % 24 == 0
            ? $"{link.LifetimeHours / 24} 天"
            : $"{link.LifetimeHours} 小时";
        var downloads = link.MaxDownloads is null
            ? "下载次数不限"
            : $"最多下载 {link.MaxDownloads} 次";
        return $"{lifetime}内有效，{downloads}";
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
        var snapshot = await dbContext.ReportSnapshots
            .AsNoTracking()
            .Where(item => item.Id == reportId)
            .Select(item => new { item.SchemaVersion, item.CanonicalJson })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ReportNotFoundException(reportId);
        return ReportCanonicalSerializer.Deserialize(snapshot.CanonicalJson, snapshot.SchemaVersion);
    }

    private async Task<Guid[]> ResolveTaskChannelIdsAsync(
        ReportRun run,
        CancellationToken cancellationToken)
    {
        if (run.RetryOfRunId is not null)
        {
            var sourceDeliveries = await dbContext.DeliveryRecords
                .AsNoTracking()
                .Where(delivery => delivery.RunId == run.RetryOfRunId.Value)
                .ToListAsync(cancellationToken);
            if (sourceDeliveries.Count > 0)
            {
                return sourceDeliveries
                    .Where(delivery => delivery.Status == DeliveryStatus.Failed)
                    .Select(delivery => delivery.ChannelId)
                    .Distinct()
                    .ToArray();
            }
        }

        return await dbContext.NotificationChannels
            .AsNoTracking()
            .Where(channel => channel.Enabled)
            .OrderBy(channel => channel.CreatedAt)
            .ThenBy(channel => channel.Id)
            .Select(channel => channel.Id)
            .ToArrayAsync(cancellationToken);
    }

    private async Task PreparePendingTaskWorkAsync(
        ReportRun run,
        ReportDocument report,
        List<DeliveryWork> work,
        CancellationToken cancellationToken)
    {
        foreach (var delivery in run.Deliveries.Where(item => item.Status == DeliveryStatus.Pending))
        {
            var channel = await dbContext.NotificationChannels
                .SingleOrDefaultAsync(item => item.Id == delivery.ChannelId, cancellationToken);
            if (channel is not { Enabled: true })
            {
                delivery.MarkSending();
                delivery.MarkFailed("channel_unavailable", null);
                continue;
            }

            var context = await CreateContextAsync(
                channel,
                report.ReportId,
                delivery.Id,
                cancellationToken);
            var sender = ResolveSender(channel.Type);
            var parts = sender.Render(report, context);
            if (!string.Equals(
                delivery.PayloadHash,
                ComputeAggregateHash(parts),
                StringComparison.Ordinal))
            {
                delivery.MarkSending();
                delivery.MarkFailed("payload_changed", null);
                continue;
            }

            work.Add(new DeliveryWork(delivery, context, sender, parts));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
            parts[0].AttachmentContent);

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
        run.ReportSnapshotId
            ?? throw new InvalidOperationException("A delivery run must reference a report snapshot."),
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
                    .ToArray(),
                delivery.DownloadGrant is null
                    ? null
                    : new ReportDownloadGrantDocument(
                        delivery.DownloadGrant.Id,
                        delivery.DownloadGrant.ExpiresAt,
                        delivery.DownloadGrant.RevokedAt,
                        delivery.DownloadGrant.DownloadCount,
                        delivery.DownloadGrant.MaxDownloads,
                        delivery.DownloadGrant.LastDownloadedAt)))
            .ToArray());

    private sealed record DeliveryWork(
        DeliveryRecord Delivery,
        ChannelDeliveryContext Context,
        IReportSender Sender,
        IReadOnlyList<OutboundPart> RenderedParts);
}
