namespace Sub2ApiReport.Application.Sub2Api;

public sealed record Sub2ApiExternalUser(
    long ExternalId,
    string Email,
    string? Username,
    string Status);
