using Sub2ApiReport.Application.Security;

namespace Sub2ApiReport.Api.Services;

internal sealed class SetupCodeBootstrapService(
    IServiceScopeFactory scopeFactory,
    ILogger<SetupCodeBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var setupService = scope.ServiceProvider.GetRequiredService<ISetupService>();
        var issue = await setupService.RotateChallengeOnStartupAsync(cancellationToken);
        if (issue is null)
        {
            return;
        }

        SetupBootstrapLog.CodeIssued(logger, issue.Code, issue.ExpiresAt);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static partial class SetupBootstrapLog
{
    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Admin setup required. One-time setup code: {SetupCode}. Expires at {ExpiresAt}.")]
    public static partial void CodeIssued(
        ILogger logger,
        string setupCode,
        DateTimeOffset expiresAt);
}
