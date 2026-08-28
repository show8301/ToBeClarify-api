using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed class AdminLoginRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed class AdminRegisterRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;

    [Required, StringLength(60, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string RoleLevel { get; init; } = string.Empty;

    [StringLength(40)]
    public string? StaffMemberId { get; init; }
}

public sealed class StaffRegisterRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string VerificationCode { get; init; } = string.Empty;
}

public sealed class AdminPasswordResetKeyRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;
}

public sealed class AdminPasswordResetRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string VerificationCode { get; init; } = string.Empty;
}

public sealed record AdminRegisterKeyDto(string Key, DateTimeOffset ExpiresAt);

public sealed record AdminPasswordResetKeyDto(string Key, DateTimeOffset ExpiresAt);

public sealed record AdminIdentityDto(
    string Id,
    string LoginName,
    string DisplayName,
    string Role,
    string RoleLabel,
    string? StaffMemberId);
