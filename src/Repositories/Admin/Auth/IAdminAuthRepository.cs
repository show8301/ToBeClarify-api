using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Auth;

public interface IAdminAuthRepository
{
    Task<AdminUserRow?> GetByLoginNameAsync(string loginName, CancellationToken cancellationToken);
    Task<AdminUserRow?> GetActiveByIdAsync(string id, CancellationToken cancellationToken);
    Task UpdateLastLoginAsync(string id, CancellationToken cancellationToken);
}
