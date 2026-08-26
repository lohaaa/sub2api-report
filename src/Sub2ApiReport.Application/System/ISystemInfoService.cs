namespace Sub2ApiReport.Application.System;

public interface ISystemInfoService
{
    Task<SystemVersionInfo> GetVersionAsync(CancellationToken cancellationToken);
}

public sealed record SystemVersionInfo(
    string Version,
    string Environment,
    string ReleaseChannel);
