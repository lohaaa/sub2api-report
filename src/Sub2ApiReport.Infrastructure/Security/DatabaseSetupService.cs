using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Domain.Audit;
using Sub2ApiReport.Domain.Security;
using Sub2ApiReport.Domain.System;
using Sub2ApiReport.Infrastructure.Identity;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Security;

internal sealed class DatabaseSetupService(
    ReportDbContext dbContext,
    UserManager<Administrator> userManager,
    TimeProvider timeProvider) : ISetupService
{
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    public async Task<SetupStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AsNoTracking().AnyAsync(cancellationToken))
        {
            return new SetupStatusSnapshot(false, null, null);
        }

        var activeChallenges = await dbContext.SetupChallenges
            .AsNoTracking()
            .Where(item => item.ConsumedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var challenge = activeChallenges.MaxBy(item => item.CreatedAt);

        return new SetupStatusSnapshot(
            true,
            challenge?.ExpiresAt,
            challenge?.LockedUntil);
    }

    public async Task<SecretCodeIssue?> RotateChallengeOnStartupAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var activeChallenges = await dbContext.SetupChallenges
            .Where(challenge => challenge.ConsumedAt == null && challenge.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in activeChallenges)
        {
            challenge.Revoke(now);
        }

        var generated = SecretCodeGenerator.Generate();
        var newChallenge = SetupChallenge.Create(generated.Hash, now);
        dbContext.SetupChallenges.Add(newChallenge);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SecretCodeIssue(generated.Code, newChallenge.ExpiresAt);
    }

    public async Task<SetupInitializationResult> InitializeAsync(
        InitializeAdministratorCommand command,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await InitializationLock.WaitAsync(cancellationToken);
        try
        {
            return await InitializeCoreAsync(command, correlationId, cancellationToken);
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private async Task<SetupInitializationResult> InitializeCoreAsync(
        InitializeAdministratorCommand command,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                SetupInitializationStatus.AlreadyInitialized,
                "系统已经完成初始化。");
        }

        var now = timeProvider.GetUtcNow();
        var activeChallenges = await dbContext.SetupChallenges
            .Where(item => item.ConsumedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var challenge = activeChallenges.MaxBy(item => item.CreatedAt);

        if (challenge is null || challenge.ExpiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                SetupInitializationStatus.Expired,
                "初始化码已过期，请重启应用生成新码。");
        }

        if (challenge.IsLocked(now))
        {
            await transaction.CommitAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                SetupInitializationStatus.Locked,
                "初始化尝试已暂时锁定。");
        }

        if (!SecretCodeGenerator.Verify(command.Code, challenge.CodeHash))
        {
            challenge.RegisterFailure(now);
            AddAudit(null, "setup.initialize", "administrator", "failed", correlationId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                challenge.IsLocked(now)
                    ? SetupInitializationStatus.Locked
                    : SetupInitializationStatus.InvalidCode,
                "初始化码无效。");
        }

        var administrator = Administrator.Create(command.Username.Trim(), now);
        var identityResult = await userManager.CreateAsync(administrator, command.Password);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                SetupInitializationStatus.InvalidAccount,
                identityResult.Errors.Select(error => error.Description).ToArray());
        }

        var systemSetting = await dbContext.SystemSettings
            .SingleAsync(setting => setting.Id == SystemSetting.SingletonId, cancellationToken);
        systemSetting.MarkInitialized(now);
        challenge.Consume(now);
        AddAudit(administrator.UserName, "setup.initialize", "administrator", "succeeded", correlationId);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SetupInitializationResult.Success;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SetupInitializationResult.Failure(
                SetupInitializationStatus.Conflict,
                "初始化状态已被其他请求更新。");
        }
    }

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
