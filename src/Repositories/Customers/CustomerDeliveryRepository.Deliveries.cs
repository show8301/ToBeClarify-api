using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Customers;

public sealed partial class CustomerDeliveryRepository
{
    private const string DeliverySelect = """
        SELECT D.ID AS Id,D.SESSION_ID AS SessionId,V.CUSTOMER_UID AS CustomerUid,
        S.GAME_ID AS GameId,S.CUSTOMER_NAME AS CustomerName,S.BUSINESS_DATE AS BusinessDate,
        D.ORDER_ID AS OrderId,O.ORDER_NUMBER AS OrderNumber,D.ORDER_ITEM_ID AS OrderItemId,
        D.TITLE AS Title,D.DESCRIPTION AS Description,D.STATUS AS Status,D.DUE_DATE AS DueDate,D.VERSION AS Version,
        D.CREATED_AT AS CreatedAt,D.UPDATED_AT AS UpdatedAt,D.DELIVERED_AT AS DeliveredAt
        FROM ART_DELIVERIES D JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=D.SESSION_ID
        LEFT JOIN CUSTOMER_VISITS V ON V.SESSION_ID=D.SESSION_ID LEFT JOIN ORDERS O ON O.ID=D.ORDER_ID
        """;
    private const string AssetColumns = "ID AS Id,KIND AS Kind,LABEL AS Label,URL AS Url,CONTENT_TYPE AS ContentType,BYTE_SIZE AS ByteSize,CREATED_AT AS CreatedAt";

    public async Task<IReadOnlyList<ArtDeliveryDto>> Deliveries(string? sessionId, string? status, string? search,
        string? uid, string? claimHash, bool publicAccess, CancellationToken ct, int limit = 200, int offset = 0)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var rows = (await c.QueryAsync<ArtDeliveryDto>(new CommandDefinition($"""
            {DeliverySelect} WHERE (@SessionId IS NULL OR D.SESSION_ID=@SessionId) AND (@Status IS NULL OR D.STATUS=@Status)
            AND (@Search IS NULL OR LOCATE(@Search,D.TITLE)>0 OR LOCATE(@Search,S.GAME_ID)>0 OR LOCATE(@Search,S.CUSTOMER_NAME)>0 OR LOCATE(@Search,V.CUSTOMER_UID)>0)
            AND (@Public=FALSE OR ((@Uid IS NOT NULL AND V.CUSTOMER_UID=@Uid) OR (@ClaimHash IS NOT NULL AND D.CLAIM_CODE_HASH=@ClaimHash)))
            ORDER BY D.CREATED_AT DESC,D.ID LIMIT @Limit OFFSET @Offset;
            """, new { SessionId = sessionId, Status = status, Search = search, Uid = uid, ClaimHash = claimHash, Public = publicAccess, Limit = limit, Offset = offset }, cancellationToken: ct))).AsList();
        var visible = rows.Where(x => !publicAccess || x.Status is "ready" or "delivered").ToArray();
        if (visible.Length > 0)
        {
            var assets = (await c.QueryAsync<AssetListRow>(new CommandDefinition($"SELECT DELIVERY_ID AS DeliveryId,{AssetColumns} FROM ART_DELIVERY_ASSETS WHERE DELIVERY_ID IN @Ids AND IS_REMOVED=FALSE ORDER BY CREATED_AT,ID;", new { Ids = visible.Select(x => x.Id).ToArray() }, cancellationToken: ct))).ToLookup(x => x.DeliveryId);
            foreach (var row in visible) row.Assets = assets[row.Id].Select(x => new ArtDeliveryAssetDto
            {
                Id = x.Id, Kind = x.Kind, Label = x.Label, ContentType = x.ContentType, ByteSize = x.ByteSize, CreatedAt = x.CreatedAt,
                Url = x.Kind == "image" ? $"/api/{(publicAccess ? "client" : "admin")}/art-deliveries/{row.Id}/assets/{x.Id}" : x.Url
            }).ToArray();
        }
        return rows;
    }

    public async Task<ArtDeliveryDto> Delivery(string id, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<ArtDeliveryDto>(new CommandDefinition($"{DeliverySelect} WHERE D.ID=@Id;", new { Id = id }, cancellationToken: ct))
            ?? throw new NotFoundException("找不到委託。", "DELIVERY_NOT_FOUND");
        row.Assets = await Assets(c, id, false, ct);
        return row;
    }

    private static async Task<IReadOnlyList<ArtDeliveryAssetDto>> Assets(MySqlConnection c, string id, bool client, CancellationToken ct)
    {
        var rows = (await c.QueryAsync<ArtDeliveryAssetDto>(new CommandDefinition($"SELECT {AssetColumns} FROM ART_DELIVERY_ASSETS WHERE DELIVERY_ID=@Id AND IS_REMOVED=FALSE ORDER BY CREATED_AT,ID;", new { Id = id }, cancellationToken: ct))).AsList();
        foreach (var row in rows.Where(x => x.Kind == "image")) row.Url = $"/api/{(client ? "client" : "admin")}/art-deliveries/{id}/assets/{row.Id}";
        return rows;
    }

    public async Task<string> CreateDelivery(CreateArtDeliveryRequest r, string claimHash, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT ID FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@Id FOR UPDATE;", new { Id = r.SessionId }, tx, cancellationToken: ct)) is null)
            throw new NotFoundException("找不到入場資料。", "ORDER_SESSION_NOT_FOUND");
        if (r.OrderId is not null && await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ORDERS WHERE ID=@Id AND SESSION_ID=@SessionId;", new { Id = r.OrderId, r.SessionId }, tx, cancellationToken: ct)) == 0)
            throw new BusinessException("訂單不屬於此入場資料。", "DELIVERY_ORDER_MISMATCH");
        if (r.OrderItemId is not null && (r.OrderId is null || await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ORDER_ITEMS WHERE ID=@Id AND ORDER_ID=@OrderId;", new { Id = r.OrderItemId, r.OrderId }, tx, cancellationToken: ct)) == 0))
            throw new BusinessException("訂單明細不屬於此訂單。", "DELIVERY_ITEM_MISMATCH");
        var id = Guid.NewGuid().ToString();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ART_DELIVERIES (ID,SESSION_ID,ORDER_ID,ORDER_ITEM_ID,TITLE,DESCRIPTION,DUE_DATE,CLAIM_CODE_HASH,CREATED_AT,UPDATED_AT,CREATED_BY,UPDATED_BY)
            VALUES (@Id,@SessionId,@OrderId,@OrderItemId,@Title,@Description,@DueDate,@Hash,NOW(6),NOW(6),@ActorId,@ActorId);
            """, new { Id = id, r.SessionId, r.OrderId, r.OrderItemId, Title = r.Title.Trim(), Description = r.Description?.Trim(), DueDate = r.DueDate?.ToDateTime(TimeOnly.MinValue), Hash = claimHash, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, "created", actorId, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task UpdateDelivery(string id, UpdateArtDeliveryRequest r, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var row = await LockDelivery(c, tx, id, ct);
        if (row.Version != r.Version) throw new ConflictException("委託已更新，請重新整理。", "VERSION_CONFLICT");
        if (r.Status is "ready" or "delivered" && await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ART_DELIVERY_ASSETS WHERE DELIVERY_ID=@Id AND IS_REMOVED=FALSE;", new { Id = id }, tx, cancellationToken: ct)) == 0)
            throw new BusinessException("請先上傳作品或加入雲端連結。", "DELIVERY_ASSET_REQUIRED");
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE ART_DELIVERIES SET TITLE=@Title,DESCRIPTION=@Description,STATUS=@Status,DUE_DATE=@DueDate,
            VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId,
            DELIVERED_AT=CASE WHEN @Status='delivered' THEN COALESCE(DELIVERED_AT,NOW(6)) ELSE NULL END WHERE ID=@Id;
            """, new { Id = id, Title = r.Title.Trim(), Description = r.Description?.Trim(), r.Status, DueDate = r.DueDate?.ToDateTime(TimeOnly.MinValue), ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, $"updated_{r.Status}", actorId, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReissueCode(string id, string hash, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        _ = await LockDelivery(c, tx, id, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE ART_DELIVERIES SET CLAIM_CODE_HASH=@Hash,VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId WHERE ID=@Id;", new { Id = id, Hash = hash, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, "claim_code_reissued", actorId, ct);
        await tx.CommitAsync(ct);
    }

    public async Task AddAsset(string id, string label, string? url, byte[]? bytes, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var row = await LockDelivery(c, tx, id, ct);
        if (row.Status is "cancelled" or "delivered") throw new ConflictException("請先重新開啟委託再修改作品。", "DELIVERY_CLOSED");
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ART_DELIVERY_ASSETS WHERE DELIVERY_ID=@Id AND IS_REMOVED=FALSE;", new { Id = id }, tx, cancellationToken: ct)) >= 20)
            throw new BusinessException("每筆委託最多 20 個作品檔案或連結。", "DELIVERY_ASSET_LIMIT");
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ART_DELIVERY_ASSETS (ID,DELIVERY_ID,KIND,LABEL,URL,CONTENT_TYPE,BYTE_SIZE,FILE_BYTES,CREATED_AT,CREATED_BY)
            VALUES (@Id,@DeliveryId,@Kind,@Label,@Url,@ContentType,@Size,@Bytes,NOW(6),@ActorId);
            UPDATE ART_DELIVERIES SET STATUS='in_progress',VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId WHERE ID=@DeliveryId;
            """, new { Id = Guid.NewGuid().ToString(), DeliveryId = id, Kind = bytes is null ? "link" : "image", Label = label, Url = url, ContentType = bytes is null ? null : "image/png", Size = bytes?.Length ?? 0, Bytes = bytes, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, "asset_added", actorId, ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveAsset(string id, string assetId, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var row = await LockDelivery(c, tx, id, ct);
        if (row.Status is "cancelled" or "delivered") throw new ConflictException("請先重新開啟委託再修改作品。", "DELIVERY_CLOSED");
        if (await c.ExecuteAsync(new CommandDefinition("UPDATE ART_DELIVERY_ASSETS SET IS_REMOVED=TRUE WHERE ID=@AssetId AND DELIVERY_ID=@Id AND IS_REMOVED=FALSE;", new { Id = id, AssetId = assetId }, tx, cancellationToken: ct)) == 0)
            throw new NotFoundException("找不到作品。", "DELIVERY_ASSET_NOT_FOUND");
        await c.ExecuteAsync(new CommandDefinition("UPDATE ART_DELIVERIES SET STATUS='in_progress',VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId WHERE ID=@Id;", new { Id = id, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, "asset_removed", actorId, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<byte[]> ImageBytes(string id, string assetId, string? uid, string? claimHash, bool client, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("""
            SELECT A.FILE_BYTES FROM ART_DELIVERY_ASSETS A JOIN ART_DELIVERIES D ON D.ID=A.DELIVERY_ID
            LEFT JOIN CUSTOMER_VISITS V ON V.SESSION_ID=D.SESSION_ID
            WHERE A.ID=@AssetId AND D.ID=@Id AND A.IS_REMOVED=FALSE AND A.KIND='image'
            AND (@Client=FALSE OR (D.STATUS IN ('ready','delivered') AND ((@Uid IS NOT NULL AND V.CUSTOMER_UID=@Uid) OR (@Hash IS NOT NULL AND D.CLAIM_CODE_HASH=@Hash))));
            """, new { Id = id, AssetId = assetId, Uid = uid, Hash = claimHash, Client = client }, cancellationToken: ct))
            ?? throw new NotFoundException("找不到作品或憑證已失效。", "DELIVERY_ASSET_NOT_FOUND");
    }

    public async Task Acknowledge(string id, string? uid, string? hash, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var row = await LockDelivery(c, tx, id, ct);
        var authorized = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM ART_DELIVERIES D LEFT JOIN CUSTOMER_VISITS V ON V.SESSION_ID=D.SESSION_ID
            WHERE D.ID=@Id AND ((@Uid IS NOT NULL AND V.CUSTOMER_UID=@Uid) OR (@Hash IS NOT NULL AND D.CLAIM_CODE_HASH=@Hash));
            """, new { Id = id, Uid = uid, Hash = hash }, tx, cancellationToken: ct));
        if (authorized == 0) throw new NotFoundException("找不到委託或憑證已失效。", "DELIVERY_NOT_FOUND");
        if (row.Status is not ("ready" or "delivered")) throw new ConflictException("作品尚未開放領取。", "DELIVERY_NOT_READY");
        if (row.Status != "delivered")
        {
            await c.ExecuteAsync(new CommandDefinition("UPDATE ART_DELIVERIES SET STATUS='delivered',DELIVERED_AT=NOW(6),VERSION=VERSION+1,UPDATED_AT=NOW(6) WHERE ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
            await Audit(c, tx, "delivery", id, "customer_acknowledged", uid, ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task<LockedDelivery> LockDelivery(MySqlConnection c, MySqlTransaction tx, string id, CancellationToken ct)
        => await c.QuerySingleOrDefaultAsync<LockedDelivery>(new CommandDefinition("SELECT STATUS AS Status,VERSION AS Version FROM ART_DELIVERIES WHERE ID=@Id FOR UPDATE;", new { Id = id }, tx, cancellationToken: ct))
            ?? throw new NotFoundException("找不到委託。", "DELIVERY_NOT_FOUND");
    private sealed class LockedDelivery
    {
        public string Status { get; set; } = "";
        public int Version { get; set; }
    }
    private sealed class AssetListRow
    {
        public string DeliveryId { get; set; } = "";
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Url { get; set; }
        public string? ContentType { get; set; }
        public long ByteSize { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
