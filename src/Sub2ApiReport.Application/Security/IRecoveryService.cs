namespace Sub2ApiReport.Application.Security;

public interface IRecoveryService
{
    Task<SecretCodeIssue?> CreateChallengeAsync(
        string? correlationId,
        CancellationToken cancellationToken);

    Task<AccountRecoveryResult> RecoverAsync(
        RecoverAdministratorCommand command,
        string? correlationId,
        CancellationToken cancellationToken);
}

public sealed record RecoverAdministratorCommand(
    string Username,
    string Code,
    string NewPassword);

public sealed record AccountRecoveryResult(
    AccountRecoveryStatus Status,
    IReadOnlyList<string> Errors)
{
    public static AccountRecoveryResult Success { get; } = new(
        AccountRecoveryStatus.Succeeded,
        []);

    public static AccountRecoveryResult Failure(
        AccountRecoveryStatus status,
        params string[] errors) => new(status, errors);
}

public enum AccountRecoveryStatus
{
    Succeeded,
    NotInitialized,
    InvalidCode,
    Expired,
    Locked,
    InvalidAccount,
    Conflict,
}
