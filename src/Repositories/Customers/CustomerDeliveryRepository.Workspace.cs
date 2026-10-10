using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Customers;

public sealed partial class CustomerDeliveryRepository
{
    private static Task<bool> HasWorkspace(MySqlConnection c, CancellationToken ct, MySqlTransaction? tx = null)
        => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='ART_DELIVERY_WORKFLOWS');", transaction: tx, cancellationToken: ct));
    private static async Task RequireWorkspace(MySqlConnection c, CancellationToken ct, MySqlTransaction? tx = null)
    {
        if (!await HasWorkspace(c, ct, tx)) throw new BusinessException("交付工作台資料服務尚未啟用。", "DELIVERY_WORKSPACE_UNAVAILABLE");
    }
    private static async Task ValidateStaff(MySqlConnection c, MySqlTransaction tx, string? staffId, CancellationToken ct)
    {
        if (staffId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM STAFF_MEMBERS WHERE ID=@Id AND IS_ACTIVE=TRUE);", new { Id = staffId }, tx, cancellationToken: ct)))
            throw new BusinessException("請選擇啟用中的人員。", "DELIVERY_STAFF_INVALID");
    }
    private static async Task FillWorkflow(MySqlConnection c, IReadOnlyList<ArtDeliveryDto> deliveries, CancellationToken ct)
    {
        if (deliveries.Count == 0 || !await HasWorkspace(c, ct)) return;
        var rows = (await c.QueryAsync<WorkflowRow>(new CommandDefinition("""
            SELECT W.DELIVERY_ID AS Id,W.ASSIGNED_STAFF_ID AS StaffId,S.DISPLAY_NAME AS StaffName,
            W.CLAIM_CODE_ENCRYPTED IS NOT NULL AS CanViewCode,W.NOTIFIED_AT AS NotifiedAt
            FROM ART_DELIVERY_WORKFLOWS W LEFT JOIN STAFF_MEMBERS S ON S.ID=W.ASSIGNED_STAFF_ID WHERE W.DELIVERY_ID IN @Ids;
            """, new { Ids = deliveries.Select(x => x.Id).ToArray() }, cancellationToken: ct))).ToDictionary(x => x.Id);
        foreach (var d in deliveries)
        {
            d.WorkspaceAvailable = true;
            if (!rows.TryGetValue(d.Id, out var row)) continue;
            d.AssignedStaffId = row.StaffId; d.AssignedStaffName = row.StaffName;
            d.CanViewClaimCode = row.CanViewCode; d.NotifiedAt = row.NotifiedAt;
        }
    }
    public async Task<DeliveryWorkspaceDto> Workspace(string? sessionId, string? status, string? search,
        string scope, string? staffId, string sort, int page, int size, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await RequireWorkspace(c, ct);
        const string filter = """
            LEFT JOIN ART_DELIVERY_WORKFLOWS W ON W.DELIVERY_ID=D.ID
            WHERE (@SessionId IS NULL OR D.SESSION_ID=@SessionId) AND (@Status IS NULL OR D.STATUS=@Status)
            AND (@Search IS NULL OR LOCATE(@Search,D.TITLE)>0 OR LOCATE(@Search,S.GAME_ID)>0 OR LOCATE(@Search,S.CUSTOMER_NAME)>0 OR LOCATE(@Search,V.CUSTOMER_UID)>0)
            AND (@Scope='all' OR (@Scope='mine' AND W.ASSIGNED_STAFF_ID=@StaffId) OR (@Scope='unassigned' AND W.ASSIGNED_STAFF_ID IS NULL))
            """;
        var args = new { SessionId = sessionId, Status = status, Search = search, Scope = scope, StaffId = staffId, Limit = size, Offset = (page - 1) * size };
        var order = sort == "due" ? "D.DUE_DATE IS NULL,D.DUE_DATE,D.UPDATED_AT DESC,D.ID" : "D.UPDATED_AT DESC,D.ID";
        var rows = (await c.QueryAsync<ArtDeliveryDto>(new CommandDefinition($"{DeliverySelect} {filter} ORDER BY {order} LIMIT @Limit OFFSET @Offset;", args, cancellationToken: ct))).AsList();
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM ART_DELIVERIES D JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=D.SESSION_ID LEFT JOIN CUSTOMER_VISITS V ON V.SESSION_ID=D.SESSION_ID {filter};", args, cancellationToken: ct));
        if (rows.Count > 0)
        {
            var assets = (await c.QueryAsync<AssetListRow>(new CommandDefinition($"SELECT DELIVERY_ID AS DeliveryId,{AssetColumns} FROM ART_DELIVERY_ASSETS WHERE DELIVERY_ID IN @Ids AND IS_REMOVED=FALSE ORDER BY CREATED_AT,ID;", new { Ids = rows.Select(x => x.Id).ToArray() }, cancellationToken: ct))).ToLookup(x => x.DeliveryId);
            foreach (var row in rows) row.Assets = assets[row.Id].Select(x => new ArtDeliveryAssetDto
            {
                Id = x.Id, Kind = x.Kind, Label = x.Label, ContentType = x.ContentType, ByteSize = x.ByteSize, CreatedAt = x.CreatedAt,
                Url = x.Kind == "image" ? $"/api/admin/art-deliveries/{row.Id}/assets/{x.Id}" : x.Url
            }).ToArray();
        }
        await FillWorkflow(c, rows, ct);
        var staff = (await c.QueryAsync<DeliveryStaffDto>(new CommandDefinition("SELECT ID AS Id,DISPLAY_NAME AS DisplayName FROM STAFF_MEMBERS WHERE IS_ACTIVE=TRUE ORDER BY SORT_ORDER,DISPLAY_NAME,ID;", cancellationToken: ct))).AsList();
        return new(rows, total, page, size, staff);
    }
    private static async Task SaveCode(MySqlConnection c, MySqlTransaction tx, string id, string cipher, string? assignedStaffId, bool creating, CancellationToken ct)
    {
        var available = await HasWorkspace(c, ct, tx);
        if (!available)
        {
            if (assignedStaffId is not null) throw new BusinessException("交付工作台資料服務尚未啟用。", "DELIVERY_WORKSPACE_UNAVAILABLE");
            return; // Existing clients remain functional before the additive migration.
        }
        if (creating) await ValidateStaff(c, tx, assignedStaffId, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ART_DELIVERY_WORKFLOWS (DELIVERY_ID,CLAIM_CODE_ENCRYPTED,ASSIGNED_STAFF_ID)
            VALUES (@Id,@Cipher,@StaffId) ON DUPLICATE KEY UPDATE CLAIM_CODE_ENCRYPTED=@Cipher;
            """, new { Id = id, Cipher = cipher, StaffId = assignedStaffId }, tx, cancellationToken: ct));
    }
    private static async Task ClearNotification(MySqlConnection c, MySqlTransaction tx, string id, CancellationToken ct)
    {
        if (await HasWorkspace(c, ct, tx)) await c.ExecuteAsync(new CommandDefinition(
            "UPDATE ART_DELIVERY_WORKFLOWS SET NOTIFIED_AT=NULL,NOTIFIED_BY=NULL WHERE DELIVERY_ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
    }
    public async Task Assign(string id, AssignDeliveryRequest r, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RequireWorkspace(c, ct, tx);
        var d = await LockDelivery(c, tx, id, ct);
        if (d.Version != r.Version) throw new ConflictException("委託已更新，請重新整理。", "VERSION_CONFLICT");
        await ValidateStaff(c, tx, r.StaffMemberId, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ART_DELIVERY_WORKFLOWS (DELIVERY_ID,ASSIGNED_STAFF_ID) VALUES (@Id,@StaffId)
            ON DUPLICATE KEY UPDATE ASSIGNED_STAFF_ID=@StaffId;
            UPDATE ART_DELIVERIES SET VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId WHERE ID=@Id;
            """, new { Id = id, StaffId = r.StaffMemberId, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "delivery", id, "assignment_changed", actorId, ct);
        await tx.CommitAsync(ct);
    }
    public async Task Notify(string id, int version, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RequireWorkspace(c, ct, tx);
        var d = await LockDelivery(c, tx, id, ct);
        if (d.Version != version) throw new ConflictException("委託已更新，請重新整理。", "VERSION_CONFLICT");
        if (d.Status != "ready") throw new ConflictException("作品須先開放領取。", "DELIVERY_NOT_READY");
        var notified = await c.ExecuteScalarAsync<DateTime?>(new CommandDefinition("SELECT NOTIFIED_AT FROM ART_DELIVERY_WORKFLOWS WHERE DELIVERY_ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
        if (notified is null)
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ART_DELIVERY_WORKFLOWS (DELIVERY_ID,NOTIFIED_AT,NOTIFIED_BY) VALUES (@Id,NOW(6),@ActorId)
                ON DUPLICATE KEY UPDATE NOTIFIED_AT=NOW(6),NOTIFIED_BY=@ActorId;
                UPDATE ART_DELIVERIES SET VERSION=VERSION+1,UPDATED_AT=NOW(6),UPDATED_BY=@ActorId WHERE ID=@Id;
                """, new { Id = id, ActorId = actorId }, tx, cancellationToken: ct));
            await Audit(c, tx, "delivery", id, "customer_notified", actorId, ct);
        }
        await tx.CommitAsync(ct);
    }
    public async Task<string> ViewClaimCode(string id, string actorId, Func<string, string, string> reveal, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RequireWorkspace(c, ct, tx);
        _ = await LockDelivery(c, tx, id, ct);
        var row = await c.QuerySingleAsync<StoredCode>(new CommandDefinition("""
            SELECT D.CLAIM_CODE_HASH AS Hash,W.CLAIM_CODE_ENCRYPTED AS Cipher FROM ART_DELIVERIES D
            LEFT JOIN ART_DELIVERY_WORKFLOWS W ON W.DELIVERY_ID=D.ID WHERE D.ID=@Id;
            """, new { Id = id }, tx, cancellationToken: ct));
        if (row.Cipher is null) throw new ConflictException("舊作品只有領取碼雜湊，請由店經理重發一次後啟用檢視。", "DELIVERY_CODE_LEGACY");
        var code = reveal(row.Cipher, row.Hash);
        await Audit(c, tx, "delivery", id, "claim_code_viewed", actorId, ct);
        await tx.CommitAsync(ct);
        return code;
    }
    public async Task<IReadOnlyList<DeliveryHistoryDto>> DeliveryHistory(string id, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM ART_DELIVERIES WHERE ID=@Id);", new { Id = id }, cancellationToken: ct)))
            throw new NotFoundException("找不到委託。", "DELIVERY_NOT_FOUND");
        return (await c.QueryAsync<DeliveryHistoryDto>(new CommandDefinition("""
            SELECT A.ID AS Id,A.ACTION AS Action,A.CREATED_AT AS CreatedAt,
            CASE WHEN A.ACTION='customer_acknowledged' THEN '顧客' ELSE COALESCE(S.DISPLAY_NAME,'歷史操作人員') END AS ActorName
            FROM CUSTOMER_DELIVERY_AUDIT A LEFT JOIN ADMIN_USERS U ON U.ID=A.ACTOR_ID LEFT JOIN STAFF_MEMBERS S ON S.ID=U.STAFF_MEMBER_ID
            WHERE A.ENTITY_TYPE='delivery' AND A.ENTITY_ID=@Id ORDER BY A.CREATED_AT DESC,A.ID DESC LIMIT 200;
            """, new { Id = id }, cancellationToken: ct))).AsList();
    }
    private sealed class WorkflowRow
    {
        public string Id { get; set; } = "";
        public string? StaffId { get; set; }
        public string? StaffName { get; set; }
        public bool CanViewCode { get; set; }
        public DateTime? NotifiedAt { get; set; }
    }
    private sealed class StoredCode
    {
        public string Hash { get; set; } = "";
        public string? Cipher { get; set; }
    }
}
