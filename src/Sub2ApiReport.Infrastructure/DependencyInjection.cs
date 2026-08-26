using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.People;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Application.System;
using Sub2ApiReport.Infrastructure.Audit;
using Sub2ApiReport.Infrastructure.Identity;
using Sub2ApiReport.Infrastructure.People;
using Sub2ApiReport.Infrastructure.Persistence;
using Sub2ApiReport.Infrastructure.Reports;
using Sub2ApiReport.Infrastructure.Security;
using Sub2ApiReport.Infrastructure.Sub2Api;
using Sub2ApiReport.Infrastructure.System;

namespace Sub2ApiReport.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ReportDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton(TimeProvider.System);

        services.AddIdentityCore<Administrator>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.User.RequireUniqueEmail = false;
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<ReportDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserClaimsPrincipalFactory<Administrator>, AdministratorClaimsPrincipalFactory>();

        services.AddScoped<IAuditWriter, DatabaseAuditWriter>();
        services.AddScoped<ISetupService, DatabaseSetupService>();
        services.AddScoped<IRecoveryService, DatabaseRecoveryService>();
        services.AddScoped<IPeopleService, DatabasePeopleService>();
        services.AddScoped<ISub2ApiConnectionService, DatabaseSub2ApiConnectionService>();
        services.AddScoped<IKeyInventoryService, DatabaseKeyInventoryService>();
        services.AddScoped<IReportService, DatabaseReportService>();
        services.AddHttpClient<ISub2ApiClient, Sub2ApiClient>(client =>
            client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            })
            .RedactLoggedHeaders(["x-api-key"]);
        services.AddScoped<ISystemSettingsService, DatabaseSystemSettingsService>();
        return services;
    }
}
