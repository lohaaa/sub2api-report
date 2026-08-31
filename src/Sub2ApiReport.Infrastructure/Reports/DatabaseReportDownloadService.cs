using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.Reports;

internal sealed class DatabaseReportDownloadService(
    ReportDbContext dbContext,
    ReportDownloadTokenProtector protector,
    ISystemSettingsService settingsService,
    TimeProvider timeProvider) : IReportDownloadService
{
    public async Task<ReportDownloadLink?> PrepareLinkAsync(
        Guid reportId,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        if (settings.ReportExternalBaseUrl is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var grant = dbContext.ReportDownloadGrants.Local
            .SingleOrDefault(item => item.DeliveryId == deliveryId)
            ?? await dbContext.ReportDownloadGrants
                .SingleOrDefaultAsync(item => item.DeliveryId == deliveryId, cancellationToken);
        string token;
        if (grant is null)
        {
            token = CreateToken();
            grant = ReportDownloadGrant.Create(
                deliveryId,
                reportId,
                ComputeTokenHash(token),
                protector.Protect(token),
                settings.ReportDownloadLinkHours,
                settings.ReportDownloadMaxDownloads,
                now);
            dbContext.ReportDownloadGrants.Add(grant);
        }
        else if (!grant.IsPending && !grant.IsAvailable(now))
        {
            token = CreateToken();
            grant.Rotate(
                ComputeTokenHash(token),
                protector.Protect(token),
                settings.ReportDownloadLinkHours,
                settings.ReportDownloadMaxDownloads,
                now);
        }
        else
        {
            token = protector.Unprotect(grant.TokenCiphertext);
        }

        var url = $"{settings.ReportExternalBaseUrl}/api/v1/report-downloads/xlsx?token="
            + Uri.EscapeDataString(token);
        return new ReportDownloadLink(
            grant.Id,
            url,
            grant.LifetimeHours,
            grant.MaxDownloads);
    }

    public async Task ActivateAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var grant = dbContext.ReportDownloadGrants.Local
            .SingleOrDefault(item => item.DeliveryId == deliveryId)
            ?? await dbContext.ReportDownloadGrants
                .SingleOrDefaultAsync(item => item.DeliveryId == deliveryId, cancellationToken);
        grant?.Activate(timeProvider.GetUtcNow());
    }

    public async Task<ReportDownloadAttempt> DownloadAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            return new ReportDownloadAttempt(ReportDownloadAttemptStatus.Invalid);
        }

        var tokenHash = ComputeTokenHash(token);
        var grant = await dbContext.ReportDownloadGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (grant is null)
        {
            return new ReportDownloadAttempt(ReportDownloadAttemptStatus.Invalid);
        }

        var now = timeProvider.GetUtcNow();
        var updated = await dbContext.ReportDownloadGrants
            .Where(item => item.Id == grant.Id
                && item.RevokedAt == null
                && item.ExpiresAt != null
                && item.ExpiresAt > now
                && (item.MaxDownloads == null || item.DownloadCount < item.MaxDownloads))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.DownloadCount, item => item.DownloadCount + 1)
                    .SetProperty(item => item.LastDownloadedAt, now),
                cancellationToken);
        if (updated != 1)
        {
            return new ReportDownloadAttempt(ReportDownloadAttemptStatus.Inactive);
        }

        var snapshot = await dbContext.ReportSnapshots
            .AsNoTracking()
            .Where(item => item.Id == grant.ReportSnapshotId)
            .Select(item => new { item.SchemaVersion, item.CanonicalJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
        {
            return new ReportDownloadAttempt(ReportDownloadAttemptStatus.Invalid);
        }

        var report = ReportCanonicalSerializer.Deserialize(snapshot.CanonicalJson, snapshot.SchemaVersion);
        return new ReportDownloadAttempt(
            ReportDownloadAttemptStatus.Available,
            ReportXlsxSerializer.Serialize(report),
            ReportXlsxFileName.Create(report));
    }

    public async Task<bool> RevokeAsync(
        Guid reportId,
        Guid grantId,
        CancellationToken cancellationToken)
    {
        var grant = await dbContext.ReportDownloadGrants
            .SingleOrDefaultAsync(
                item => item.Id == grantId && item.ReportSnapshotId == reportId,
                cancellationToken);
        if (grant is null)
        {
            return false;
        }

        grant.Revoke(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string CreateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string ComputeTokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
