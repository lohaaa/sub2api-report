using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sub2ApiReport.Infrastructure.Persistence;

public sealed class DesignTimeReportDbContextFactory : IDesignTimeDbContextFactory<ReportDbContext>
{
    public ReportDbContext CreateDbContext(string[] args)
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("SUB2API_REPORT_DATABASE")
            ?? DatabaseDefaults.ConnectionString;
        var connectionString = DatabaseDefaults.ResolveConnectionString(
            configuredConnectionString,
            Directory.GetCurrentDirectory());
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ReportDbContext(options);
    }
}
