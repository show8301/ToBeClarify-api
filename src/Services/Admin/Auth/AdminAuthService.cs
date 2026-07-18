using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Auth;

namespace ToBeClarify.Api.Services.Admin.Auth;

public sealed class AdminAuthService : IAdminAuthService
{
    private readonly IAdminAuthRepository _repository;
    private readonly PasswordHashService _passwordHashService;
    private readonly JwtTokenService _jwtTokenService;

    public AdminAuthService(
        IAdminAuthRepository repository,
        PasswordHashService passwordHashService,
        JwtTokenService jwtTokenService)
    {
        _repository = repository;
        _passwordHashService = passwordHashService;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<(AdminIdentityDto Identity, string Token)> LoginAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedLoginName = loginName.Trim();
        var user = await _repository.GetByLoginNameAsync(normalizedLoginName, cancellationToken);

        if (user is null || !user.IsActive || !AdminRole.IsValid(user.RoleLevel) ||
            !_passwordHashService.Verify(password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid login credentials.", "INVALID_CREDENTIALS");
        }

        await _repository.UpdateLastLoginAsync(user.Id, cancellationToken);

        var identity = ToIdentity(user);
        return (identity, _jwtTokenService.CreateAdminToken(user));
    }

    public async Task<AdminIdentityDto> GetCurrentIdentityAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedException();

        var user = await _repository.GetActiveByIdAsync(userId, cancellationToken);
        if (user is null || !AdminRole.IsValid(user.RoleLevel))
            throw new UnauthorizedException();

        return ToIdentity(user);
    }

    private static AdminIdentityDto ToIdentity(AdminUserRow user) =>
        new(user.Id, user.LoginName, user.DisplayName, user.RoleLevel, AdminRole.GetLabel(user.RoleLevel));
}
