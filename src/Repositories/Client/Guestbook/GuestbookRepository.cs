using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Guestbook;

public sealed class GuestbookRepository : DapperRepositoryBase, IGuestbookRepository
{
    public GuestbookRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<int> CountGuestbookCommentsAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(*) FROM `GUESTBOOK_COMMENTS` WHERE `IS_VISIBLE` = TRUE;";
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GuestbookCommentRow>> GetGuestbookCommentsAsync(int offset, int pageSize, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `DISPLAY_NAME` AS DisplayName, `CONTENT` AS Content,
                   `IS_PINNED` AS IsPinned, `CREATED_AT` AS CreatedAt
            FROM `GUESTBOOK_COMMENTS`
            WHERE `IS_VISIBLE` = TRUE
            ORDER BY `IS_PINNED` DESC, `SORT_ORDER`, `CREATED_AT` DESC
            LIMIT @PageSize OFFSET @Offset;
            """;
        return await QueryAsync<GuestbookCommentRow>(sql, new { Offset = offset, PageSize = pageSize }, cancellationToken);
    }

    public async Task<GuestbookCommentRow?> GetGuestbookCommentAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `DISPLAY_NAME` AS DisplayName, `CONTENT` AS Content,
                   `IS_PINNED` AS IsPinned, `CREATED_AT` AS CreatedAt
            FROM `GUESTBOOK_COMMENTS`
            WHERE `ID` = @Id AND `IS_VISIBLE` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<GuestbookCommentRow>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IReadOnlyList<GuestbookReplyRow>> GetGuestbookRepliesAsync(IReadOnlyCollection<string> commentIds, CancellationToken cancellationToken)
    {
        if (commentIds.Count == 0) return Array.Empty<GuestbookReplyRow>();
        const string sql = """
            SELECT `ID` AS Id, `COMMENT_ID` AS CommentId, `DISPLAY_NAME` AS DisplayName,
                   `CONTENT` AS Content, `CREATED_AT` AS CreatedAt
            FROM `GUESTBOOK_REPLIES`
            WHERE `COMMENT_ID` IN @CommentIds AND `IS_VISIBLE` = TRUE
            ORDER BY `CREATED_AT`;
            """;
        return await QueryAsync<GuestbookReplyRow>(sql, new { CommentIds = commentIds }, cancellationToken);
    }

    public async Task InsertGuestbookCommentAsync(string id, string displayName, string? userToken, string content, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `GUESTBOOK_COMMENTS`
                (`ID`, `DISPLAY_NAME`, `USER_TOKEN`, `CONTENT`, `IS_PINNED`, `IS_VISIBLE`, `SORT_ORDER`, `CREATED_AT`, `UPDATED_AT`)
            VALUES
                (@Id, @DisplayName, @UserToken, @Content, FALSE, TRUE, 0, @Now, @Now);
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, DisplayName = displayName, UserToken = userToken, Content = content, Now = now }, cancellationToken: cancellationToken));
    }

    public async Task<bool> InsertGuestbookReplyAsync(string id, string commentId, string displayName, string? userToken, string content, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT COUNT(*) > 0 FROM `GUESTBOOK_COMMENTS` WHERE `ID` = @CommentId AND `IS_VISIBLE` = TRUE;",
            new { CommentId = commentId }, transaction, cancellationToken: cancellationToken));
        if (!exists)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        const string sql = """
            INSERT INTO `GUESTBOOK_REPLIES`
                (`ID`, `COMMENT_ID`, `DISPLAY_NAME`, `USER_TOKEN`, `CONTENT`, `IS_VISIBLE`, `CREATED_AT`, `UPDATED_AT`)
            VALUES
                (@Id, @CommentId, @DisplayName, @UserToken, @Content, TRUE, @Now, @Now);
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, CommentId = commentId, DisplayName = displayName, UserToken = userToken, Content = content, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
