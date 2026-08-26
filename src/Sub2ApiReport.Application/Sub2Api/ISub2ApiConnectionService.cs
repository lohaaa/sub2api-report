namespace Sub2ApiReport.Application.Sub2Api;

public interface ISub2ApiConnectionService
{
    Task<Sub2ApiConnectionSnapshot?> GetAsync(CancellationToken cancellationToken);

    Task<Sub2ApiConnectionSnapshot> SaveAsync(
        SaveSub2ApiConnectionCommand command,
        CancellationToken cancellationToken);

    Task<Sub2ApiConnectionCredentials> GetCredentialsAsync(CancellationToken cancellationToken);

    Task RecordTestResultAsync(
        bool succeeded,
        string code,
        CancellationToken cancellationToken);
}

public sealed record Sub2ApiConnectionSnapshot(
    string BaseUrl,
    bool HasAdminApiKey,
    string? AdminApiKeyMask,
    long UserId,
    long? CodexGroupId,
    long Revision,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestCode,
    DateTimeOffset? LastSynchronizedAt,
    int? LastSynchronizedKeyCount);

public sealed record SaveSub2ApiConnectionCommand(
    string BaseUrl,
    string? AdminApiKey,
    bool ClearAdminApiKey,
    long UserId,
    long? CodexGroupId,
    long ExpectedRevision);

public sealed record Sub2ApiConnectionCredentials(
    string BaseUrl,
    string AdminApiKey,
    long UserId,
    long? CodexGroupId,
    long Revision);

public sealed class Sub2ApiConnectionNotConfiguredException()
    : InvalidOperationException("The Sub2API connection is not fully configured.");

public sealed class Sub2ApiConnectionConflictException(long expectedRevision, long actualRevision)
    : InvalidOperationException(
        $"The Sub2API connection revision changed from {expectedRevision} to {actualRevision}.");
