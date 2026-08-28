using Microsoft.AspNetCore.DataProtection;

namespace Sub2ApiReport.Infrastructure.Reports;

internal sealed class ReportDownloadTokenProtector
{
    public const string ProtectorPurpose = "Sub2ApiReport.Reports.DownloadToken.v1";

    private readonly IDataProtector _protector;

    public ReportDownloadTokenProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
