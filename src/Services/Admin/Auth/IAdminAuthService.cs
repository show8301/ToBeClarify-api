using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Auth;

public interface IAdminAuthService
{
    Task<(AdminIdentityDto Identity, string Token)> LoginAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken);

    Task<AdminIdentityDto> RegisterAsync(
        AdminRegisterRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken);

    Task<AdminRegisterKeyDto> IssueRegisterKeyAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken);

    Task<AdminIdentityDto> RegisterStaffAsync(
        StaffRegisterRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminStaffListItemDto>> GetAllStaffListAsync(
        CancellationToken cancellationToken);

    Task<AdminPasswordResetKeyDto> IssuePasswordResetKeyAsync(
        AdminPasswordResetKeyRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        AdminPasswordResetRequest request,
        CancellationToken cancellationToken);

    Task<AdminIdentityDto> GetCurrentIdentityAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
