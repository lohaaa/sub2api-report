namespace Sub2ApiReport.Application.Sub2Api;

public interface ISub2ApiClient
{
    Task<Sub2ApiConnectionProbe> TestAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken);
}

public sealed record Sub2ApiConnectionProbe(long AvailableKeyCount);

public sealed record Sub2ApiExternalKey(
    long ExternalId,
    string Name,
    string Status,
    long? GroupId,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum Sub2ApiFailureKind
{
    Unauthorized,
    Forbidden,
    Incompatible,
    RateLimited,
    Timeout,
    Unavailable,
    InvalidResponse,
}

public sealed class Sub2ApiClientException(
    Sub2ApiFailureKind kind,
    string message,
    TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public Sub2ApiFailureKind Kind { get; } = kind;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
