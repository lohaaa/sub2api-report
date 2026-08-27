namespace Sub2ApiReport.Application.Sub2Api;

public interface ISub2ApiClient
{
    Task<Sub2ApiConnectionProbe> TestAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Sub2ApiExternalUser>> GetUsersAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
        Sub2ApiConnectionCredentials connection,
        long externalUserId,
        CancellationToken cancellationToken);

    Task<Sub2ApiUsageStats> GetUsageStatsAsync(
        Sub2ApiConnectionCredentials connection,
        long externalUserId,
        long externalApiKeyId,
        DateOnly startDate,
        DateOnly endDate,
        string timezone,
        CancellationToken cancellationToken);
}

public sealed record Sub2ApiConnectionProbe(long AvailableUserCount);

public sealed record Sub2ApiExternalKey(
    long ExternalId,
    string Name,
    string Status,
    long? GroupId,
    DateTimeOffset? LastUsedAt);

public sealed record Sub2ApiUsageStats(
    long TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheTokens,
    long TotalCacheCreationTokens,
    long TotalCacheReadTokens,
    long TotalTokens,
    decimal TotalCost,
    decimal TotalActualCost,
    decimal AverageDurationMs);

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
