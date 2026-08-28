using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Auth;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/auth")]
public sealed class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _authService;
    private readonly AdminAuthOptions _options;
    private readonly IWebHostEnvironment _environment;

    public AdminAuthController(
        IAdminAuthService authService,
        IOptions<AdminAuthOptions> options,
        IWebHostEnvironment environment)
    {
        _authService = authService;
        _options = options.Value;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("admin-login")]
    [ProducesResponseType(typeof(ApiResponse<AdminIdentityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminIdentityDto>>> Login(
        [FromBody] AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        var (identity, token) = await _authService.LoginAsync(request.LoginName, request.Password, cancellationToken);
        Response.Cookies.Append(AdminAuthConstants.CookieName, token, CreateCookieOptions());
        return Ok(ApiResponse<AdminIdentityDto>.Ok(identity));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting("admin-register")]
    [ProducesResponseType(typeof(ApiResponse<AdminIdentityDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AdminIdentityDto>>> Register(
        [FromBody] StaffRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var identity = await _authService.RegisterStaffAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<AdminIdentityDto>.Ok(identity));
    }

    [Authorize(Policy = "AdminManager")]
    [HttpPost("register-key")]
    [ProducesResponseType(typeof(ApiResponse<AdminRegisterKeyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminRegisterKeyDto>>> GetRegisterKey(CancellationToken cancellationToken)
    {
        var key = await _authService.IssueRegisterKeyAsync(User, cancellationToken);
        return Ok(ApiResponse<AdminRegisterKeyDto>.Ok(key));
    }

    [Authorize(Policy = "AdminManager")]
    [HttpPost("password-reset-key")]
    [ProducesResponseType(typeof(ApiResponse<AdminPasswordResetKeyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminPasswordResetKeyDto>>> GetPasswordResetKey(
        [FromBody] AdminPasswordResetKeyRequest request,
        CancellationToken cancellationToken)
    {
        var key = await _authService.IssuePasswordResetKeyAsync(request, User, cancellationToken);
        return Ok(ApiResponse<AdminPasswordResetKeyDto>.Ok(key));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password/reset")]
    [EnableRateLimiting("admin-password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] AdminPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AdminAuthConstants.AdminPolicy)]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<AdminIdentityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminIdentityDto>>> Me(CancellationToken cancellationToken)
    {
        var identity = await _authService.GetCurrentIdentityAsync(User, cancellationToken);
        return Ok(ApiResponse<AdminIdentityDto>.Ok(identity));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<bool>> Logout()
    {
        Response.Cookies.Delete(AdminAuthConstants.CookieName, CreateCookieOptions());
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private CookieOptions CreateCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = _environment.IsProduction() || Request.IsHttps || _options.CrossSiteCookie,
        SameSite = _options.CrossSiteCookie ? SameSiteMode.None : SameSiteMode.Lax,
        Path = "/api",
        MaxAge = TimeSpan.FromMinutes(Math.Max(5, _options.TokenLifetimeMinutes))
    };
}
