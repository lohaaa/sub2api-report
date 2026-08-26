using System.ComponentModel.DataAnnotations;

namespace Sub2ApiReport.Api.Models;

/// <summary>Describes whether the instance still requires its first administrator.</summary>
public sealed record SetupStatusResponse(
    bool SetupRequired,
    DateTimeOffset? ChallengeExpiresAt,
    DateTimeOffset? LockedUntil);

/// <summary>Creates the only administrator using the one-time setup code.</summary>
public sealed record InitializeAdministratorRequest
{
    [Required, StringLength(64, MinimumLength = 16)]
    public required string Code { get; init; }

    [Required, StringLength(64, MinimumLength = 3)]
    public required string Username { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public required string Password { get; init; }
}

/// <summary>Contains an antiforgery request token for the current browser session.</summary>
public sealed record AntiforgeryTokenResponse(string Token);

/// <summary>Authenticates the administrator with a username and password.</summary>
public sealed record LoginRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public required string Username { get; init; }

    [Required, StringLength(128, MinimumLength = 1)]
    public required string Password { get; init; }
}

/// <summary>Describes the current authenticated administrator session.</summary>
public sealed record CurrentAdministratorResponse(
    string Username,
    DateTimeOffset SessionStartedAt,
    DateTimeOffset? StepUpExpiresAt);

/// <summary>Changes the current administrator password.</summary>
public sealed record ChangePasswordRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string CurrentPassword { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public required string NewPassword { get; init; }
}

/// <summary>Confirms the current password for a short high-risk operation window.</summary>
public sealed record StepUpRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Password { get; init; }
}

/// <summary>Resets the administrator password with a host-generated recovery code.</summary>
public sealed record RecoverAdministratorRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public required string Username { get; init; }

    [Required, StringLength(64, MinimumLength = 16)]
    public required string Code { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public required string NewPassword { get; init; }
}
