using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Domain.Audit;
using Sub2ApiReport.Domain.Security;
using Sub2ApiReport.Infrastructure.Identity;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Security;

internal sealed class DatabaseRecoveryService(
    ReportDbContext dbContext,
    UserManager<Administrator> userManager,
    TimeProvider timeProvider) : IRecoveryService
{
    public async Task<SecretCodeIssue?> CreateChallengeAsync(
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var administrator = await dbContext.Users.SingleOrDefaultAsync(cancellationToken);
        if (administrator is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var activeChallenges = await dbContext.RecoveryChallenges
            .Where(challenge => challenge.ConsumedAt == null && challenge.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in activeChallenges)
        {
            challenge.Revoke(now);
        }

        var generated = SecretCodeGenerator.Generate();
        var newChallenge = RecoveryChallenge.Create(administrator.Id, generated.Hash, now);
        dbContext.RecoveryChallenges.Add(newChallenge);
        AddAudit(administrator.UserName, "auth.recovery-code.create", "administrator", "succeeded", correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SecretCodeIssue(generated.Code, newChallenge.ExpiresAt);
    }

    public async Task<AccountRecoveryResult> RecoverAsync(
        RecoverAdministratorCommand command,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var administrator = await userManager.FindByNameAsync(command.Username.Trim());
        if (administrator is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return InvalidCode();
        }

        var now = timeProvider.GetUtcNow();
        var activeChallenges = await dbContext.RecoveryChallenges
            .Where(item => item.AdministratorId == administrator.Id
                && item.ConsumedAt == null
                && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var challenge = activeChallenges.MaxBy(item => item.CreatedAt);

        if (challenge is null || challenge.ExpiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return AccountRecoveryResult.Failure(
                AccountRecoveryStatus.Expired,
                "恢复码已过期，请在主机重新生成。");
        }

        if (challenge.IsLocked(now))
        {
            await transaction.CommitAsync(cancellationToken);
            return AccountRecoveryResult.Failure(
                AccountRecoveryStatus.Locked,
                "恢复尝试已暂时锁定。");
        }

        if (!SecretCodeGenerator.Verify(command.Code, challenge.CodeHash))
        {
            challenge.RegisterFailure(now);
            AddAudit(administrator.UserName, "auth.recover", "administrator", "failed", correlationId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return challenge.IsLocked(now)
                ? AccountRecoveryResult.Failure(
                    AccountRecoveryStatus.Locked,
                    "恢复尝试已暂时锁定。")
                : InvalidCode();
        }

        var removeResult = await userManager.RemovePasswordAsync(administrator);
        if (!removeResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityFailure(removeResult);
        }

        var addResult = await userManager.AddPasswordAsync(administrator, command.NewPassword);
        if (!addResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityFailure(addResult);
        }

        challenge.Consume(now);
        AddAudit(administrator.UserName, "auth.recover", "administrator", "succeeded", correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountRecoveryResult.Success;
    }

    private static AccountRecoveryResult InvalidCode() => AccountRecoveryResult.Failure(
        AccountRecoveryStatus.InvalidCode,
        "用户名或恢复码无效。");

    private static AccountRecoveryResult IdentityFailure(IdentityResult result) =>
        AccountRecoveryResult.Failure(
            AccountRecoveryStatus.InvalidAccount,
            result.Errors.Select(error => error.Description).ToArray());

    private void AddAudit(
        string? actor,
        string action,
        string target,
        string result,
        string? correlationId) =>
        dbContext.AuditEvents.Add(AuditEvent.Create(
            timeProvider.GetUtcNow(),
            actor,
            action,
            target,
            result,
            correlationId));
}
