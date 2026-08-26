using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sub2ApiReport.Application.Security;
using Sub2ApiReport.Infrastructure;
using Sub2ApiReport.Infrastructure.Persistence;

if (args is not ["admin", "create-reset-code"])
{
    Console.Error.WriteLine("Usage: appctl admin create-reset-code");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var configuredConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? DatabaseDefaults.ConnectionString;
var connectionString = DatabaseDefaults.ResolveConnectionString(
    configuredConnectionString,
    builder.Environment.ContentRootPath);
var configuredDataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine("data", "keys");
var dataProtectionKeysPath = DatabaseDefaults.ResolvePath(
    configuredDataProtectionKeysPath,
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("Sub2ApiReport")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.AddInfrastructure(connectionString);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var recoveryService = scope.ServiceProvider.GetRequiredService<IRecoveryService>();
var issue = await recoveryService.CreateChallengeAsync(
    correlationId: "host-cli",
    CancellationToken.None);

if (issue is null)
{
    Console.Error.WriteLine("The administrator has not been initialized.");
    return 1;
}

Console.WriteLine($"Administrator recovery code: {issue.Code}");
Console.WriteLine($"Expires at: {issue.ExpiresAt:O}");
return 0;
