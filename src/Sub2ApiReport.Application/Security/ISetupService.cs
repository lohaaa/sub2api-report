namespace Sub2ApiReport.Application.Security;

public interface ISetupService
{
    Task<SetupStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken);

    Task<SecretCodeIssue?> RotateChallengeOnStartupAsync(CancellationToken cancellationToken);

    Task<SetupInitializationResult> InitializeAsync(
        InitializeAdministratorCommand command,
        string? correlationId,
        CancellationToken cancellationToken);
}

public sealed record SetupStatusSnapshot(
    bool SetupRequired,
    DateTimeOffset? ChallengeExpiresAt,
    DateTimeOffset? LockedUntil);

public sealed record SecretCodeIssue(string Code, DateTimeOffset ExpiresAt);

public sealed record InitializeAdministratorCommand(
    string Code,
    string Username,
    string Password);

public sealed record SetupInitializationResult(
    SetupInitializationStatus Status,
    IReadOnlyList<string> Errors)
{
    public static SetupInitializationResult Success { get; } = new(
        SetupInitializationStatus.Succeeded,
        []);

    public static SetupInitializationResult Failure(
        SetupInitializationStatus status,
        params string[] errors) => new(status, errors);
}

public enum SetupInitializationStatus
{
    Succeeded,
    AlreadyInitialized,
    InvalidCode,
    Expired,
    Locked,
    InvalidAccount,
    Conflict,
}
