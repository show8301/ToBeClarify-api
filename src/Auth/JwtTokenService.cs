using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ToBeClarify.Api.Models.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ToBeClarify.Api.Auth;

public sealed class JwtTokenService
{
    private readonly JwtAuthOptions _options;

    public JwtTokenService(IOptions<JwtAuthOptions> options)
    {
        _options = options.Value;
    }

    public string CreateAdminToken(AdminUserRow user, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(AdminAuthConstants.RoleClaimType, user.RoleLevel),
            new("token_version", user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(2)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
