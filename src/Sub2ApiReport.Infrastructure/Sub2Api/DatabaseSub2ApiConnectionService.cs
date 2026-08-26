using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.Sub2Api;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Sub2Api;

internal sealed class DatabaseSub2ApiConnectionService(
    ReportDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : ISub2ApiConnectionService
{
    private const string ProtectorPurpose = "Sub2ApiReport.Sub2Api.AdminApiKey.v1";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<Sub2ApiConnectionSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        var connection = await dbContext.Sub2ApiConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
        return connection is null ? null : Map(connection);
    }

    public async Task<Sub2ApiConnectionSnapshot> SaveAsync(
        SaveSub2ApiConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(command.BaseUrl);
        var secret = NormalizeSecret(command.AdminApiKey);
        var connection = await dbContext.Sub2ApiConnections.SingleOrDefaultAsync(
            item => item.Id == Sub2ApiConnection.SingletonId,
            cancellationToken);

        if (connection is null)
        {
            if (command.ExpectedRevision != 0)
            {
                throw new Sub2ApiConnectionConflictException(command.ExpectedRevision, 0);
            }

            if (command.ClearAdminApiKey || secret is null)
            {
                throw new ArgumentException(
                    "An Admin API Key is required when creating the connection.",
                    nameof(command));
            }

            connection = Sub2ApiConnection.Create(
                baseUrl,
                protector.Protect(secret),
                CreateSuffix(secret),
                command.UserId,
                command.CodexGroupId,
                timeProvider.GetUtcNow());
            dbContext.Sub2ApiConnections.Add(connection);
        }
        else
        {
            if (connection.Revision != command.ExpectedRevision)
            {
                throw new Sub2ApiConnectionConflictException(
                    command.ExpectedRevision,
                    connection.Revision);
            }

            connection.Update(
                baseUrl,
                secret is null ? null : protector.Protect(secret),
                secret is null ? null : CreateSuffix(secret),
                command.ClearAdminApiKey,
                command.UserId,
                command.CodexGroupId,
                timeProvider.GetUtcNow());
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new Sub2ApiConnectionConflictException(
                command.ExpectedRevision,
                command.ExpectedRevision + 1);
        }

        return Map(connection);
    }

    public async Task<Sub2ApiConnectionCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Sub2ApiConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == Sub2ApiConnection.SingletonId,
                cancellationToken);
        if (connection?.AdminApiKeyCiphertext is null)
        {
            throw new Sub2ApiConnectionNotConfiguredException();
        }

        return new Sub2ApiConnectionCredentials(
            connection.BaseUrl,
            protector.Unprotect(connection.AdminApiKeyCiphertext),
            connection.UserId,
            connection.CodexGroupId,
            connection.Revision);
    }

    public async Task RecordTestResultAsync(
        bool succeeded,
        string code,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Sub2ApiConnections.SingleOrDefaultAsync(
            item => item.Id == Sub2ApiConnection.SingletonId,
            cancellationToken);
        if (connection is null)
        {
            throw new Sub2ApiConnectionNotConfiguredException();
        }

        connection.RecordConnectionTest(succeeded, code, timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new Sub2ApiConnectionConflictException(
                connection.Revision,
                connection.Revision + 1);
        }
    }

    private static Sub2ApiConnectionSnapshot Map(Sub2ApiConnection connection) => new(
        connection.BaseUrl,
        connection.AdminApiKeyCiphertext is not null,
        connection.AdminApiKeySuffix is null ? null : $"****{connection.AdminApiKeySuffix}",
        connection.UserId,
        connection.CodexGroupId,
        connection.Revision,
        connection.UpdatedAt,
        connection.LastTestedAt,
        connection.LastTestSucceeded,
        connection.LastTestCode,
        connection.LastSynchronizedAt,
        connection.LastSynchronizedKeyCount);

    private static string NormalizeBaseUrl(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "The Base URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.",
                nameof(value));
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string? NormalizeSecret(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length is < 8 or > 4096)
        {
            throw new ArgumentException(
                "The Admin API Key must contain between 8 and 4096 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string CreateSuffix(string secret) => secret[^Math.Min(4, secret.Length)..];
}
