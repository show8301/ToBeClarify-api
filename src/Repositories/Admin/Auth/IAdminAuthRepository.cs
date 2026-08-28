using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Auth;

public interface IAdminAuthRepository
{
    Task<AdminUserRow?> GetByLoginNameAsync(string loginName, CancellationToken cancellationToken);
    Task<AdminUserRow?> GetActiveByIdAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserListRow>> GetAllStaffListAsync(CancellationToken cancellationToken);
    Task<AdminTokenStateRow?> GetTokenStateByIdAsync(string id, CancellationToken cancellationToken);
    Task<bool> StaffMemberExistsAsync(string id, CancellationToken cancellationToken);
    Task CreateAsync(string id, string loginName, string displayName, string passwordHash, string roleLevel,
        string? staffMemberId, string actorId, CancellationToken cancellationToken);
    Task CreateStaffAccountAsync(string adminId, string staffMemberId, string loginName, string displayName,
        string passwordHash, CancellationToken cancellationToken);
    Task<bool> ResetPasswordAsync(string id, string passwordHash, string updatedBy, CancellationToken cancellationToken);
    Task UpdateLastLoginAsync(string id, CancellationToken cancellationToken);
}
