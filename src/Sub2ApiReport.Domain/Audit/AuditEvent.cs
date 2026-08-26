namespace Sub2ApiReport.Domain.Audit;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public Guid Id { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    public string? Actor { get; private init; }

    public string Action { get; private init; } = string.Empty;

    public string Target { get; private init; } = string.Empty;

    public string Result { get; private init; } = string.Empty;

    public string? CorrelationId { get; private init; }

    public string? MetadataJson { get; private init; }

    public static AuditEvent Create(
        DateTimeOffset occurredAt,
        string? actor,
        string action,
        string target,
        string result,
        string? correlationId = null,
        string? metadataJson = null) => new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Actor = NormalizeOptional(actor),
            Action = Validate(action, 100, nameof(action)),
            Target = Validate(target, 200, nameof(target)),
            Result = Validate(result, 32, nameof(result)),
            CorrelationId = NormalizeOptional(correlationId),
            MetadataJson = NormalizeOptional(metadataJson),
        };

    private static string Validate(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
