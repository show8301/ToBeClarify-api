using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Auth;

public interface IAdminAuthService
{
    Task<(AdminIdentityDto Identity, string Token)> LoginAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken);

    Task<AdminIdentityDto> GetCurrentIdentityAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
