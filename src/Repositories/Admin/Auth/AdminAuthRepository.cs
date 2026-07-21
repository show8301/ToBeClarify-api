using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Auth;

public sealed class AdminAuthRepository : DapperRepositoryBase, IAdminAuthRepository
{
    public AdminAuthRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<AdminUserRow?> GetByLoginNameAsync(string loginName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `LOGIN_NAME` AS LoginName, `DISPLAY_NAME` AS DisplayName,
                   `PASSWORD_HASH` AS PasswordHash, `ROLE_LEVEL` AS RoleLevel,
                   `STAFF_MEMBER_ID` AS StaffMemberId, `IS_ACTIVE` AS IsActive,
                   `TOKEN_VERSION` AS TokenVersion
            FROM `ADMIN_USERS`
            WHERE `LOGIN_NAME` = @LoginName
            LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<AdminUserRow>(sql, new { LoginName = loginName }, cancellationToken);
    }

    public async Task<AdminUserRow?> GetActiveByIdAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `LOGIN_NAME` AS LoginName, `DISPLAY_NAME` AS DisplayName,
                   `PASSWORD_HASH` AS PasswordHash, `ROLE_LEVEL` AS RoleLevel,
                   `STAFF_MEMBER_ID` AS StaffMemberId, `IS_ACTIVE` AS IsActive,
                   `TOKEN_VERSION` AS TokenVersion
            FROM `ADMIN_USERS`
            WHERE `ID` = @Id AND `IS_ACTIVE` = TRUE
            LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<AdminUserRow>(sql, new { Id = id }, cancellationToken);
    }

    public async Task UpdateLastLoginAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `ADMIN_USERS`
            SET `LAST_LOGIN_AT` = CURRENT_TIMESTAMP,
                `UPDATED_BY` = @Id,
                `UPDATED_AT` = CURRENT_TIMESTAMP
            WHERE `ID` = @Id;
            """;

        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }
}
