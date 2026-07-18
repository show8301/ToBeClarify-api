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
        Secure = _environment.IsProduction() || Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/api",
        MaxAge = TimeSpan.FromMinutes(Math.Max(5, _options.TokenLifetimeMinutes))
    };
}
