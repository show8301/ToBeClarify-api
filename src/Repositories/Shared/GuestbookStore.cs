using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Shared;

// Shared persistence boundary for public reads/writes and authenticated moderation.
public sealed class GuestbookStore(AppDbContext db)
{
    private const string Columns = """
        ID AS Id, DISPLAY_NAME AS DisplayName, CONTENT AS Content, AUTHOR_TYPE AS AuthorType,
        IS_VISIBLE AS IsVisible, VERSION AS Version, CREATED_AT AS CreatedAt, EDITED_AT AS EditedAt,
        CUSTOMER_UID AS CustomerUid, IMAGE_ID AS ImageId
        """;
    private const string ThreadColumns = Columns + ", ID AS ThreadId, IS_PINNED AS IsPinned, SORT_ORDER AS SortOrder, ALLOW_REPLIES AS AllowReplies";
    private const string ReplyColumns = Columns + ", COMMENT_ID AS ThreadId";

    public async Task<GuestbookList> List(
        int page,
        int size,
        bool admin,
        string filter,
        CancellationToken ct,
        string? cursor = null,
        string? viewerKey = null,
        string? searchTerm = null,
        string hasImage = "all")
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var where = admin ? filter switch
        {
            "hidden" => "IS_VISIBLE=FALSE",
            "locked" => "ALLOW_REPLIES=FALSE",
            "hidden-replies" => "EXISTS(SELECT 1 FROM GUESTBOOK_REPLIES HR WHERE HR.COMMENT_ID=GUESTBOOK_COMMENTS.ID AND HR.IS_VISIBLE=FALSE)",
            _ => "TRUE"
        } : "IS_VISIBLE=TRUE";
        if (admin && !string.IsNullOrWhiteSpace(searchTerm))
        {
            where += """
                 AND (
                    LOCATE(LOWER(@Search), LOWER(DISPLAY_NAME)) > 0
                    OR LOCATE(LOWER(@Search), LOWER(CONTENT)) > 0
                    OR LOCATE(LOWER(@Search), LOWER(COALESCE(CUSTOMER_UID, ''))) > 0
                    OR EXISTS (
                        SELECT 1 FROM GUESTBOOK_REPLIES SR
                        WHERE SR.COMMENT_ID=GUESTBOOK_COMMENTS.ID
                          AND (
                            LOCATE(LOWER(@Search), LOWER(SR.DISPLAY_NAME)) > 0
                            OR LOCATE(LOWER(@Search), LOWER(SR.CONTENT)) > 0
                            OR LOCATE(LOWER(@Search), LOWER(COALESCE(SR.CUSTOMER_UID, ''))) > 0
                          )
                    )
                 )
                """;
        }
        if (admin)
        {
            where += hasImage switch
            {
                "yes" => " AND (IMAGE_ID IS NOT NULL OR EXISTS(SELECT 1 FROM GUESTBOOK_REPLIES IR WHERE IR.COMMENT_ID=GUESTBOOK_COMMENTS.ID AND IR.IMAGE_ID IS NOT NULL))",
                "no" => " AND IMAGE_ID IS NULL AND NOT EXISTS(SELECT 1 FROM GUESTBOOK_REPLIES IR WHERE IR.COMMENT_ID=GUESTBOOK_COMMENTS.ID AND IR.IMAGE_ID IS NOT NULL)",
                _ => ""
            };
        }
        var visibleReplies = admin ? "" : " AND R.IS_VISIBLE=TRUE";
        var hiddenReplyCount = admin
            ? ", (SELECT COUNT(*) FROM GUESTBOOK_REPLIES HR WHERE HR.COMMENT_ID=GUESTBOOK_COMMENTS.ID AND HR.IS_VISIBLE=FALSE) AS HiddenReplyCount"
            : "";
        var select = $"SELECT {ThreadColumns}, (SELECT COUNT(*) FROM GUESTBOOK_REPLIES R WHERE R.COMMENT_ID=GUESTBOOK_COMMENTS.ID{visibleReplies}) AS ReplyCount, (SELECT COUNT(*) FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_COMMENTS.ID) AS LikeCount, EXISTS(SELECT 1 FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_COMMENTS.ID AND L.VISITOR_KEY=@ViewerKey) AS ViewerLiked{hiddenReplyCount} FROM GUESTBOOK_COMMENTS";
        var parameters = new { ViewerKey = viewerKey, Search = searchTerm };
        var pins = (await c.QueryAsync<GuestbookMessage>(new CommandDefinition($"{select} WHERE {where} AND IS_PINNED=TRUE ORDER BY SORT_ORDER, CREATED_AT DESC, ID DESC;", parameters, cancellationToken: ct))).AsList();
        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM GUESTBOOK_COMMENTS WHERE {where} AND IS_PINNED=FALSE;", parameters, cancellationToken: ct));
        if (cursor is not null && (cursor.Length == 0 || cursor.Length > 40)) throw new BusinessException("分頁游標不正確。", "INVALID_CURSOR");
        var anchor = cursor is null ? null : await Get(c, null, cursor, false, true, false, ct);
        var seek = anchor is null ? "" : " AND (CREATED_AT<@CreatedAt OR (CREATED_AT=@CreatedAt AND ID<@Id))";
        var rows = (await c.QueryAsync<GuestbookMessage>(new CommandDefinition($"{select} WHERE {where} AND IS_PINNED=FALSE{seek} ORDER BY CREATED_AT DESC, ID DESC LIMIT @Size OFFSET @Offset;", new { Size = size + 1, Offset = cursor is null ? (page - 1) * size : 0, CreatedAt = anchor?.CreatedAt, Id = anchor?.Id, ViewerKey = viewerKey, Search = searchTerm }, cancellationToken: ct))).AsList();
        var more = rows.Count > size;
        if (more) rows.RemoveAt(size);
        return new(page, size, count, rows, pins, more ? rows[^1].Id : null);
    }

    public async Task<GuestbookMessage> Get(string id, bool reply, bool admin, CancellationToken ct, string? viewerKey = null)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        if (!admin)
        {
            var table = reply ? "GUESTBOOK_REPLIES" : "GUESTBOOK_COMMENTS";
            var columns = reply ? ReplyColumns : ThreadColumns;
            var row = await c.QuerySingleOrDefaultAsync<GuestbookMessage>(new CommandDefinition($"SELECT {columns}, (SELECT COUNT(*) FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID={table}.ID) AS LikeCount, EXISTS(SELECT 1 FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID={table}.ID AND L.VISITOR_KEY=@ViewerKey) AS ViewerLiked FROM {table} WHERE ID=@Id AND IS_VISIBLE=TRUE;", new { Id = id, ViewerKey = viewerKey }, cancellationToken: ct));
            return row ?? throw new NotFoundException("找不到留言。", "GUESTBOOK_NOT_FOUND");
        }
        return await Get(c, null, id, reply, admin, false, ct);
    }

    public async Task<GuestbookMessage> GetReply(string threadId, string replyId, string? viewerKey, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<GuestbookMessage>(new CommandDefinition($"SELECT {ReplyColumns}, (SELECT COUNT(*) FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_REPLIES.ID) AS LikeCount, EXISTS(SELECT 1 FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_REPLIES.ID AND L.VISITOR_KEY=@ViewerKey) AS ViewerLiked FROM GUESTBOOK_REPLIES WHERE GUESTBOOK_REPLIES.ID=@ReplyId AND GUESTBOOK_REPLIES.COMMENT_ID=@ThreadId AND GUESTBOOK_REPLIES.IS_VISIBLE=TRUE AND EXISTS(SELECT 1 FROM GUESTBOOK_COMMENTS C WHERE C.ID=GUESTBOOK_REPLIES.COMMENT_ID AND C.IS_VISIBLE=TRUE);", new { ThreadId = threadId, ReplyId = replyId, ViewerKey = viewerKey }, cancellationToken: ct));
        return row ?? throw new NotFoundException("找不到回覆。", "GUESTBOOK_NOT_FOUND");
    }

    private static async Task<GuestbookMessage> Get(MySqlConnection c, MySqlTransaction? tx, string id, bool reply, bool admin, bool locking, CancellationToken ct)
    {
        var table = reply ? "GUESTBOOK_REPLIES" : "GUESTBOOK_COMMENTS";
        var visible = admin ? "" : " AND IS_VISIBLE=TRUE";
        var row = await c.QuerySingleOrDefaultAsync<GuestbookMessage>(new CommandDefinition(
            $"SELECT {(reply ? ReplyColumns : ThreadColumns)} FROM {table} WHERE ID=@Id{visible}{(locking ? " FOR UPDATE" : "")};", new { Id = id }, tx, cancellationToken: ct));
        return row ?? throw new NotFoundException("找不到留言。", "GUESTBOOK_NOT_FOUND");
    }

    public async Task<GuestbookReplies> Replies(string id, int page, int size, bool admin, CancellationToken ct, string? cursor = null, string? viewerKey = null)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        // The public lookup must also verify parent visibility.
        await Get(c, null, id, false, admin, false, ct);
        var where = "COMMENT_ID=@Id" + (admin ? "" : " AND IS_VISIBLE=TRUE");
        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM GUESTBOOK_REPLIES WHERE {where};", new { Id = id }, cancellationToken: ct));
        if (cursor is not null && (cursor.Length == 0 || cursor.Length > 40)) throw new BusinessException("分頁游標不正確。", "INVALID_CURSOR");
        var anchor = cursor is null ? null : await Get(c, null, cursor, true, true, false, ct);
        if (anchor is not null && anchor.ThreadId != id) throw new BusinessException("回覆游標不正確。", "INVALID_CURSOR");
        var seek = anchor is null ? "" : " AND (CREATED_AT>@CreatedAt OR (CREATED_AT=@CreatedAt AND ID>@CursorId))";
        var rows = (await c.QueryAsync<GuestbookMessage>(new CommandDefinition($"SELECT {ReplyColumns}, (SELECT COUNT(*) FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_REPLIES.ID) AS LikeCount, EXISTS(SELECT 1 FROM GUESTBOOK_LIKES L WHERE L.MESSAGE_ID=GUESTBOOK_REPLIES.ID AND L.VISITOR_KEY=@ViewerKey) AS ViewerLiked FROM GUESTBOOK_REPLIES WHERE {where}{seek} ORDER BY CREATED_AT, ID LIMIT @Size OFFSET @Offset;", new { Id = id, Size = size + 1, Offset = cursor is null ? (page - 1) * size : 0, CreatedAt = anchor?.CreatedAt, CursorId = anchor?.Id, ViewerKey = viewerKey }, cancellationToken: ct))).AsList();
        var more = rows.Count > size;
        if (more) rows.RemoveAt(size);
        return new(page, size, count, rows, more ? rows[^1].Id : null);
    }

    public async Task<GuestbookLikeResult> SetLike(string id, string viewerKey, bool liked, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var exists = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM GUESTBOOK_COMMENTS WHERE ID=@Id AND IS_VISIBLE=TRUE)
              OR EXISTS(SELECT 1 FROM GUESTBOOK_REPLIES R JOIN GUESTBOOK_COMMENTS C ON C.ID=R.COMMENT_ID
                        WHERE R.ID=@Id AND R.IS_VISIBLE=TRUE AND C.IS_VISIBLE=TRUE);
            """, new { Id = id }, tx, cancellationToken: ct));
        if (!exists) throw new NotFoundException("找不到留言。", "GUESTBOOK_NOT_FOUND");
        if (liked)
            await c.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO GUESTBOOK_LIKES (MESSAGE_ID,VISITOR_KEY,CREATED_AT) VALUES (@Id,@ViewerKey,UTC_TIMESTAMP(6));", new { Id = id, ViewerKey = viewerKey }, tx, cancellationToken: ct));
        else
            await c.ExecuteAsync(new CommandDefinition("DELETE FROM GUESTBOOK_LIKES WHERE MESSAGE_ID=@Id AND VISITOR_KEY=@ViewerKey;", new { Id = id, ViewerKey = viewerKey }, tx, cancellationToken: ct));
        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM GUESTBOOK_LIKES WHERE MESSAGE_ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new(count, liked);
    }

    public async Task<GuestbookSettings> Settings(CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return await c.QuerySingleAsync<GuestbookSettings>(new CommandDefinition("SELECT MASCOT_NAME AS MascotName, VERSION AS Version FROM GUESTBOOK_SETTINGS WHERE ID=1;", cancellationToken: ct));
    }

    public async Task<string?> StaffName(string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT S.DISPLAY_NAME FROM ADMIN_USERS A JOIN STAFF_MEMBERS S ON S.ID=A.STAFF_MEMBER_ID WHERE A.ID=@Id AND A.IS_ACTIVE=TRUE;", new { Id = actorId }, cancellationToken: ct));
    }

    public async Task<GuestbookMessage> Create(string? threadId, string name, string content, string authorType, string? actorId, string? staffId, string? visitorKey, CancellationToken ct, string? customerUid = null, byte[]? imageBytes = null)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        // All reply creation and moderation lock the parent first, so closing a thread
        // cannot race a successful reply through a stale visibility/lock check.
        if (threadId is not null)
        {
            var parent = await Get(c, tx, threadId, false, actorId is not null, true, ct);
            if (!parent.IsVisible || !parent.AllowReplies)
                throw new ConflictException("此留言串目前不開放回覆。", "GUESTBOOK_CLOSED");
        }
        if (visitorKey is not null)
        {
            await c.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO GUESTBOOK_COOLDOWNS (VISITOR_KEY,NEXT_ALLOWED_AT) VALUES (@Key,UTC_TIMESTAMP(6));", new { Key = visitorKey }, tx, cancellationToken: ct));
            var next = await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT NEXT_ALLOWED_AT FROM GUESTBOOK_COOLDOWNS WHERE VISITOR_KEY=@Key FOR UPDATE;", new { Key = visitorKey }, tx, cancellationToken: ct));
            var utc = await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT UTC_TIMESTAMP(6);", transaction: tx, cancellationToken: ct));
            if (next > utc) throw new GuestbookRateLimitException(Math.Max(1, (int)Math.Ceiling((next - utc).TotalSeconds)));
            await c.ExecuteAsync(new CommandDefinition("UPDATE GUESTBOOK_COOLDOWNS SET NEXT_ALLOWED_AT=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 180 SECOND) WHERE VISITOR_KEY=@Key;", new { Key = visitorKey }, tx, cancellationToken: ct));
        }
        var id = Guid.NewGuid().ToString();
        var reply = threadId is not null;
        var table = reply ? "GUESTBOOK_REPLIES" : "GUESTBOOK_COMMENTS";
        var imageId = imageBytes is null ? null : Guid.NewGuid().ToString();
        if (imageBytes is not null)
        {
            await c.ExecuteAsync(new CommandDefinition(
                "INSERT INTO GUESTBOOK_IMAGES (ID,IMAGE_BYTES) VALUES (@Id,@Bytes);",
                new { Id = imageId, Bytes = imageBytes }, tx, cancellationToken: ct));
        }
        await c.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO {table} (ID,DISPLAY_NAME,CONTENT,AUTHOR_TYPE,AUTHOR_ADMIN_ID,AUTHOR_STAFF_ID,IS_VISIBLE,CREATED_AT,UPDATED_AT,CREATED_BY,UPDATED_BY,CUSTOMER_UID,IMAGE_ID,{(reply ? "COMMENT_ID" : "IS_PINNED,SORT_ORDER")})
            VALUES (@Id,@Name,@Content,@AuthorType,@ActorId,@StaffId,TRUE,NOW(6),NOW(6),@ActorId,@ActorId,@CustomerUid,@ImageId,{(reply ? "@ThreadId" : "FALSE,0")});
            """, new { Id = id, Name = name, Content = content, AuthorType = authorType, ActorId = actorId, StaffId = staffId, ThreadId = threadId, CustomerUid = customerUid, ImageId = imageId }, tx, cancellationToken: ct));
        var row = await Get(c, tx, id, reply, true, false, ct);
        if (actorId is not null) await Audit(c, tx, threadId ?? id, id, actorId, "create", null, row, ct);
        await tx.CommitAsync(ct);
        return row;
    }

    public async Task<byte[]> GetImage(string id, bool admin, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var bytes = await c.ExecuteScalarAsync<byte[]>(new CommandDefinition("""
            SELECT I.IMAGE_BYTES FROM GUESTBOOK_IMAGES I WHERE I.ID=@Id AND (
              EXISTS (SELECT 1 FROM GUESTBOOK_COMMENTS C WHERE C.IMAGE_ID=I.ID AND (@Admin OR C.IS_VISIBLE=TRUE))
              OR EXISTS (SELECT 1 FROM GUESTBOOK_REPLIES R JOIN GUESTBOOK_COMMENTS C ON C.ID=R.COMMENT_ID
                         WHERE R.IMAGE_ID=I.ID AND (@Admin OR (R.IS_VISIBLE=TRUE AND C.IS_VISIBLE=TRUE)))
            );
            """, new { Id = id, Admin = admin }, cancellationToken: ct));
        return bytes ?? throw new NotFoundException("找不到留言圖片。", "GUESTBOOK_IMAGE_NOT_FOUND");
    }

    public async Task<GuestbookMessage> Change(string threadId, string? replyId, string actor, GuestbookEdit? edit, GuestbookModerate? moderation, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var parent = await Get(c, tx, threadId, false, true, true, ct);
        var before = replyId is null ? parent : await Get(c, tx, replyId, true, true, true, ct);
        if (before.ThreadId != threadId) throw new NotFoundException("找不到回覆。", "GUESTBOOK_NOT_FOUND");
        if (before.Version != (edit?.Version ?? moderation!.Version)) throw new ConflictException("留言已被其他店員更新，請重新整理。", "VERSION_CONFLICT");
        var table = replyId is null ? "GUESTBOOK_COMMENTS" : "GUESTBOOK_REPLIES";
        var id = replyId ?? threadId;
        if (edit is not null)
        {
            if (edit.DisplayName == before.DisplayName && edit.Content == before.Content) return before;
            await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET DISPLAY_NAME=@DisplayName,CONTENT=@Content,EDITED_AT=NOW(6),UPDATED_AT=NOW(6),UPDATED_BY=@Actor,VERSION=VERSION+1 WHERE ID=@Id;", new { edit.DisplayName, edit.Content, Id = id, Actor = actor }, tx, cancellationToken: ct));
        }
        else
        {
            if (replyId is not null && (moderation!.AllowReplies.HasValue || moderation.IsPinned.HasValue)) throw new BusinessException("回覆不能置頂或設定整串狀態。");
            var extra = replyId is null ? ",ALLOW_REPLIES=COALESCE(@AllowReplies,ALLOW_REPLIES),IS_PINNED=COALESCE(@IsPinned,IS_PINNED)" : "";
            await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET IS_VISIBLE=COALESCE(@IsVisible,IS_VISIBLE){extra},UPDATED_AT=NOW(6),UPDATED_BY=@Actor,VERSION=VERSION+1 WHERE ID=@Id;", new { moderation!.IsVisible, moderation.AllowReplies, moderation.IsPinned, Id = id, Actor = actor }, tx, cancellationToken: ct));
        }
        var after = await Get(c, tx, id, replyId is not null, true, false, ct);
        await Audit(c, tx, threadId, id, actor, edit is null ? "moderate" : "edit", before, after, ct);
        await tx.CommitAsync(ct);
        return after;
    }

    public async Task<GuestbookMessage> RemoveImage(string threadId, string? replyId, int version, string actor, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var parent = await Get(c, tx, threadId, false, true, true, ct);
        var before = replyId is null ? parent : await Get(c, tx, replyId, true, true, true, ct);
        if (before.ThreadId != threadId) throw new NotFoundException("找不到回覆。", "GUESTBOOK_NOT_FOUND");
        if (before.Version != version) throw new ConflictException("留言已被其他店員更新，請重新整理。", "VERSION_CONFLICT");
        if (before.ImageId is null) throw new ConflictException("此留言已沒有圖片，請重新整理。", "VERSION_CONFLICT");

        var table = replyId is null ? "GUESTBOOK_COMMENTS" : "GUESTBOOK_REPLIES";
        var id = replyId ?? threadId;
        await c.ExecuteAsync(new CommandDefinition(
            $"UPDATE {table} SET IMAGE_ID=NULL,EDITED_AT=NOW(6),UPDATED_AT=NOW(6),UPDATED_BY=@Actor,VERSION=VERSION+1 WHERE ID=@Id;",
            new { Actor = actor, Id = id },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM GUESTBOOK_IMAGES WHERE ID=@Id;", new { Id = before.ImageId }, tx, cancellationToken: ct));
        var after = await Get(c, tx, id, replyId is not null, true, false, ct);
        await Audit(c, tx, threadId, id, actor, "image_remove", new { ImageId = before.ImageId }, new { ImageId = (string?)null }, ct);
        await tx.CommitAsync(ct);
        return after;
    }

    public async Task Reorder(IReadOnlyList<GuestbookPin> items, string actor, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var pins = (await c.QueryAsync<GuestbookMessage>(new CommandDefinition($"SELECT {ThreadColumns} FROM GUESTBOOK_COMMENTS WHERE IS_PINNED=TRUE ORDER BY ID FOR UPDATE;", transaction: tx, cancellationToken: ct))).AsList();
        if (items.Count != pins.Count || items.Select(x => x.Id).Distinct().Count() != items.Count || items.Any(x => !pins.Any(p => p.Id == x.Id && p.Version == x.Version)))
            throw new ConflictException("置頂清單已變動，請重新整理後排序。", "VERSION_CONFLICT");
        for (var i = 0; i < items.Count; i++)
        {
            var before = pins.Single(p => p.Id == items[i].Id);
            await c.ExecuteAsync(new CommandDefinition("UPDATE GUESTBOOK_COMMENTS SET SORT_ORDER=@Sort,VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@Actor WHERE ID=@Id;", new { Sort = i, before.Id, Actor = actor }, tx, cancellationToken: ct));
            await Audit(c, tx, before.Id, before.Id, actor, "reorder", new { before.SortOrder }, new { SortOrder = i }, ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task SaveSettings(GuestbookSettings settings, string actor, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var before = await c.QuerySingleAsync<GuestbookSettings>(new CommandDefinition("SELECT MASCOT_NAME AS MascotName,VERSION AS Version FROM GUESTBOOK_SETTINGS WHERE ID=1 FOR UPDATE;", transaction: tx, cancellationToken: ct));
        if (before.Version != settings.Version) throw new ConflictException("設定已被更新，請重新整理。", "VERSION_CONFLICT");
        await c.ExecuteAsync(new CommandDefinition("UPDATE GUESTBOOK_SETTINGS SET MASCOT_NAME=@MascotName,VERSION=VERSION+1 WHERE ID=1;", settings, tx, cancellationToken: ct));
        await Audit(c, tx, "settings", "settings", actor, "settings", before, settings, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<object>> History(string threadId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return (await c.QueryAsync(new CommandDefinition("SELECT ID AS id,MESSAGE_ID AS messageId,ACTOR_ID AS actorId,ACTION AS action,BEFORE_VALUE AS beforeValue,AFTER_VALUE AS afterValue,CREATED_AT AS createdAt FROM GUESTBOOK_AUDIT_LOG WHERE THREAD_ID=@Id ORDER BY ID DESC LIMIT 100;", new { Id = threadId }, cancellationToken: ct))).Cast<object>().ToArray();
    }

    private static Task Audit(MySqlConnection c, MySqlTransaction tx, string thread, string message, string actor, string action, object? before, object? after, CancellationToken ct)
        => c.ExecuteAsync(new CommandDefinition("INSERT INTO GUESTBOOK_AUDIT_LOG (THREAD_ID,MESSAGE_ID,ACTOR_ID,ACTION,BEFORE_VALUE,AFTER_VALUE,CREATED_AT) VALUES (@Thread,@Message,@Actor,@Action,@Before,@After,NOW(6));", new { Thread = thread, Message = message, Actor = actor, Action = action, Before = before is null ? null : JsonSerializer.Serialize(before), After = after is null ? null : JsonSerializer.Serialize(after) }, tx, cancellationToken: ct));
}
