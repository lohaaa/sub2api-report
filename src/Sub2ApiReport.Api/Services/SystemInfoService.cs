using System.Reflection;
using Sub2ApiReport.Application.System;

namespace Sub2ApiReport.Api.Services;

internal sealed class SystemInfoService(IWebHostEnvironment environment) : ISystemInfoService
{
    public Task<SystemVersionInfo> GetVersionAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        return Task.FromResult(new SystemVersionInfo(version, environment.EnvironmentName));
    }
}
