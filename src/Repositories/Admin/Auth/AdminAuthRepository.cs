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

    public async Task<AdminTokenStateRow?> GetTokenStateByIdAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `ROLE_LEVEL` AS RoleLevel,
                   `IS_ACTIVE` AS IsActive, `TOKEN_VERSION` AS TokenVersion
            FROM `ADMIN_USERS`
            WHERE `ID` = @Id
            LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<AdminTokenStateRow>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<bool> StaffMemberExistsAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM `STAFF_MEMBERS` WHERE `ID` = @Id);";
        return await QuerySingleOrDefaultAsync<bool>(sql, new { Id = id }, cancellationToken);
    }

    public async Task CreateAsync(string id, string loginName, string displayName, string passwordHash, string roleLevel,
        string? staffMemberId, string actorId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `ADMIN_USERS`
                (`ID`, `LOGIN_NAME`, `DISPLAY_NAME`, `PASSWORD_HASH`, `ROLE_LEVEL`, `STAFF_MEMBER_ID`,
                 `IS_ACTIVE`, `TOKEN_VERSION`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @LoginName, @DisplayName, @PasswordHash, @RoleLevel, @StaffMemberId,
                    TRUE, 1, CURRENT_TIMESTAMP, @ActorId, CURRENT_TIMESTAMP, @ActorId);
            """;

        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id, LoginName = loginName, DisplayName = displayName, PasswordHash = passwordHash,
            RoleLevel = roleLevel, StaffMemberId = staffMemberId, ActorId = actorId
        }, cancellationToken: cancellationToken));
    }

    public async Task CreateStaffAccountAsync(
        string adminId,
        string staffMemberId,
        string loginName,
        string displayName,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `STAFF_MEMBERS`
                (`ID`, `DISPLAY_NAME`, `IS_ACTIVE`, `CREATED_AT`, `UPDATED_AT`)
            VALUES (@StaffMemberId, @DisplayName, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """, new { StaffMemberId = staffMemberId, DisplayName = displayName }, transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `ADMIN_USERS`
                (`ID`, `LOGIN_NAME`, `DISPLAY_NAME`, `PASSWORD_HASH`, `ROLE_LEVEL`, `STAFF_MEMBER_ID`,
                 `IS_ACTIVE`, `TOKEN_VERSION`, `CREATED_AT`, `UPDATED_AT`)
            VALUES (@AdminId, @LoginName, @DisplayName, @PasswordHash, 'clerk', @StaffMemberId,
                    TRUE, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """, new
        {
            AdminId = adminId,
            LoginName = loginName,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            StaffMemberId = staffMemberId
        }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(
        string id,
        string passwordHash,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `ADMIN_USERS`
            SET `PASSWORD_HASH` = @PasswordHash,
                `TOKEN_VERSION` = `TOKEN_VERSION` + 1,
                `UPDATED_BY` = @UpdatedBy,
                `UPDATED_AT` = CURRENT_TIMESTAMP
            WHERE `ID` = @Id AND `IS_ACTIVE` = TRUE;
            """;

        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            PasswordHash = passwordHash,
            UpdatedBy = updatedBy
        }, cancellationToken: cancellationToken));
        return affectedRows == 1;
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
