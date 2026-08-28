using System.Security.Claims;
using MySqlConnector;
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
    private readonly IOneTimeTokenService _oneTimeTokenService;

    public AdminAuthService(
        IAdminAuthRepository repository,
        PasswordHashService passwordHashService,
        JwtTokenService jwtTokenService,
        IOneTimeTokenService oneTimeTokenService)
    {
        _repository = repository;
        _passwordHashService = passwordHashService;
        _jwtTokenService = jwtTokenService;
        _oneTimeTokenService = oneTimeTokenService;
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

    public async Task<AdminIdentityDto> RegisterAsync(
        AdminRegisterRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureDeveloper(actor);

        var loginName = request.LoginName.Trim();
        var displayName = request.DisplayName.Trim();
        var roleLevel = request.RoleLevel.Trim();
        var staffMemberId = string.IsNullOrWhiteSpace(request.StaffMemberId)
            ? null
            : request.StaffMemberId.Trim();

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(request.Password))
            throw new BusinessException("Login name, display name, and password are required.", "REGISTRATION_FIELDS_REQUIRED");

        if (!AdminRole.IsValid(roleLevel))
            throw new BusinessException("Role level must be developer, manager, or clerk.", "REGISTRATION_ROLE_NOT_ALLOWED");

        if (roleLevel == AdminRole.Clerk)
        {
            if (string.IsNullOrWhiteSpace(staffMemberId))
                throw new BusinessException("A clerk account must be linked to a staff member.", "STAFF_MEMBER_REQUIRED");

            if (!await _repository.StaffMemberExistsAsync(staffMemberId, cancellationToken))
                throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        }
        else if (staffMemberId is not null)
        {
            throw new BusinessException("Developer and manager accounts cannot be linked to a staff member.", "ADMIN_STAFF_LINK_NOT_ALLOWED");
        }

        if (await _repository.GetByLoginNameAsync(loginName, cancellationToken) is not null)
            throw new BusinessException("Login name is already in use.", "LOGIN_NAME_ALREADY_EXISTS");

        var id = Guid.NewGuid().ToString("D");
        try
        {
            await _repository.CreateAsync(id, loginName, displayName, _passwordHashService.Hash(request.Password),
                roleLevel, staffMemberId, ActorId(actor), cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new BusinessException("Login name is already in use.", "LOGIN_NAME_ALREADY_EXISTS");
        }

        var user = await _repository.GetByLoginNameAsync(loginName, cancellationToken)
            ?? throw new BusinessException("The new admin account could not be loaded.", "REGISTRATION_FAILED");
        return ToIdentity(user);
    }

    public async Task<AdminRegisterKeyDto> IssueRegisterKeyAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var result = await _oneTimeTokenService.IssueAsync(
            OneTimeTokenPurpose.StaffRegister,
            targetUserId: null,
            ActorId(actor),
            cancellationToken);
        return new AdminRegisterKeyDto(result.Key, result.ExpiresAt);
    }

    public Task<AdminIdentityDto> RegisterStaffAsync(
        StaffRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var loginName = request.LoginName.Trim();
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(request.Password))
            throw new BusinessException("帳號與密碼為必填欄位。", "REGISTRATION_FIELDS_REQUIRED");

        return _oneTimeTokenService.ConsumeAsync(
            request.VerificationCode,
            OneTimeTokenPurpose.StaffRegister,
            expectedTargetUserId: null,
            _ => CreateStaffAccountAsync(loginName, request.Password, cancellationToken),
            cancellationToken);
    }

    public async Task<AdminPasswordResetKeyDto> IssuePasswordResetKeyAsync(
        AdminPasswordResetKeyRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);

        var loginName = request.LoginName.Trim();
        var targetUser = await _repository.GetByLoginNameAsync(loginName, cancellationToken);
        if (targetUser is null || !targetUser.IsActive || !AdminRole.IsValid(targetUser.RoleLevel))
            throw new NotFoundException("找不到可重設密碼的帳號。", "PASSWORD_RESET_ACCOUNT_NOT_FOUND");

        var actorRole = actor.FindFirstValue(AdminAuthConstants.RoleClaimType);
        if (actorRole == AdminRole.Manager && targetUser.RoleLevel != AdminRole.Clerk)
        {
            throw new ForbiddenException(
                "經理只能替店員帳號產生密碼重設驗證碼。",
                "PASSWORD_RESET_TARGET_NOT_ALLOWED");
        }

        var result = await _oneTimeTokenService.IssueAsync(
            OneTimeTokenPurpose.PasswordReset,
            targetUser.Id,
            ActorId(actor),
            cancellationToken);
        return new AdminPasswordResetKeyDto(result.Key, result.ExpiresAt);
    }

    public async Task ResetPasswordAsync(
        AdminPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var loginName = request.LoginName.Trim();
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(request.NewPassword))
            throw InvalidPasswordResetKey();

        var targetUser = await _repository.GetByLoginNameAsync(loginName, cancellationToken);
        if (targetUser is null || !targetUser.IsActive || !AdminRole.IsValid(targetUser.RoleLevel))
            throw InvalidPasswordResetKey();

        await _oneTimeTokenService.ConsumeAsync(
            request.VerificationCode,
            OneTimeTokenPurpose.PasswordReset,
            targetUser.Id,
            async tokenContext =>
            {
                var updated = await _repository.ResetPasswordAsync(
                    targetUser.Id,
                    _passwordHashService.Hash(request.NewPassword),
                    tokenContext.IssuedBy,
                    cancellationToken);
                if (!updated) throw InvalidPasswordResetKey();
                return true;
            },
            cancellationToken);
    }

    private async Task<AdminIdentityDto> CreateStaffAccountAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken)
    {
        if (await _repository.GetByLoginNameAsync(loginName, cancellationToken) is not null)
            throw new BusinessException("帳號已經存在。", "LOGIN_NAME_ALREADY_EXISTS");

        var adminId = Guid.NewGuid().ToString("D");
        var staffMemberId = Guid.NewGuid().ToString("D");
        try
        {
            await _repository.CreateStaffAccountAsync(
                adminId, staffMemberId, loginName, loginName,
                _passwordHashService.Hash(password), cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new BusinessException("帳號已經存在。", "LOGIN_NAME_ALREADY_EXISTS");
        }

        var user = await _repository.GetByLoginNameAsync(loginName, cancellationToken)
            ?? throw new BusinessException("新店員帳號建立失敗。", "REGISTRATION_FAILED");
        return ToIdentity(user);
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
        new(user.Id, user.LoginName, user.DisplayName, user.RoleLevel,
            AdminRole.GetLabel(user.RoleLevel), user.StaffMemberId);

    private static void EnsureDeveloper(ClaimsPrincipal actor)
    {
        if (actor.FindFirstValue(AdminAuthConstants.RoleClaimType) != AdminRole.Developer)
            throw new ForbiddenException("This action requires developer permission.", "ADMIN_DEVELOPER_REQUIRED");
    }

    private static void EnsureManager(ClaimsPrincipal actor)
    {
        if (actor.FindFirstValue(AdminAuthConstants.RoleClaimType) is not (AdminRole.Developer or AdminRole.Manager))
            throw new ForbiddenException("This action requires manager permission.", "ADMIN_MANAGER_REQUIRED");
    }

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

    private static BusinessException InvalidPasswordResetKey()
        => new(
            "密碼重設驗證碼無效、已過期或與指定帳號不符。",
            "PASSWORD_RESET_KEY_INVALID");
}
