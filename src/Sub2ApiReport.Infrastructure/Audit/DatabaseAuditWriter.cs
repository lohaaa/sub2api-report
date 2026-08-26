using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Domain.Audit;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Audit;

internal sealed class DatabaseAuditWriter(
    ReportDbContext dbContext,
    TimeProvider timeProvider) : IAuditWriter
{
    public async Task WriteAsync(
        string? actor,
        string action,
        string target,
        string result,
        string? correlationId,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(AuditEvent.Create(
            timeProvider.GetUtcNow(),
            actor,
            action,
            target,
            result,
            correlationId,
            metadataJson));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
