using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed class AdminLoginRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string LoginName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed record AdminIdentityDto(
    string Id,
    string LoginName,
    string DisplayName,
    string Role,
    string RoleLabel);
