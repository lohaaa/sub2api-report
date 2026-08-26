namespace Sub2ApiReport.Application.Audit;

public interface IAuditWriter
{
    Task WriteAsync(
        string? actor,
        string action,
        string target,
        string result,
        string? correlationId,
        string? metadataJson,
        CancellationToken cancellationToken);
}
