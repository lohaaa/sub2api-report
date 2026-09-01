namespace Sub2ApiReport.Api.Models;

/// <summary>系统当前版本和运行通道。</summary>
public sealed record SystemVersionResponse(
    string Version,
    string Environment);
