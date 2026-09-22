using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Customers;

public sealed partial class CustomerDeliveryRepository(AppDbContext db)
{
    private const string ProfileColumns = "UID AS Uid, DISPLAY_NAME AS DisplayName, CREATED_AT AS CreatedAt";

    public async Task<CustomerProfileDto> GetProfile(string uid, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<CustomerProfileDto>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM CUSTOMER_PROFILES WHERE UID=@Uid;",
            new { Uid = uid }, cancellationToken: ct))
            ?? throw new NotFoundException("找不到顧客。", "CUSTOMER_NOT_FOUND");
    }

    public async Task<IReadOnlyList<CustomerIdentityCandidateDto>> IdentityCandidates(
        string normalizedGameId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        return (await c.QueryAsync<CustomerIdentityCandidateDto>(new CommandDefinition("""
            SELECT P.UID AS Uid,P.DISPLAY_NAME AS DisplayName,P.CREATED_AT AS CreatedAt,
                   MAX(S.CREATED_AT) AS LastVisitAt,COUNT(DISTINCT V.SESSION_ID) AS VisitCount,
                   COUNT(DISTINCT O.ID) AS OrderCount,
                   COALESCE(SUM(CASE WHEN O.ORDER_STATUS NOT IN ('cancelled','expired','rejected')
                                     THEN O.TOTAL_AMOUNT ELSE 0 END),0) AS OrderAmount
            FROM CUSTOMER_PROFILES P
            JOIN CUSTOMER_VISITS V ON V.CUSTOMER_UID=P.UID
            JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=V.SESSION_ID
            LEFT JOIN ORDERS O ON O.SESSION_ID=S.ID
            WHERE UPPER(TRIM(S.GAME_ID))=@GameId
            GROUP BY P.UID,P.DISPLAY_NAME,P.CREATED_AT
            ORDER BY LastVisitAt DESC,P.UID;
            """, new { GameId = normalizedGameId }, cancellationToken: ct))).AsList();
    }

    public async Task<CustomerProfileDto> LinkProfile(
        string sessionId, string? existingUid, string newUid, string actorId, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var name = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT CUSTOMER_NAME FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@Id FOR UPDATE;",
            new { Id = sessionId }, tx, cancellationToken: ct))
            ?? throw new NotFoundException("找不到入場資料。", "ORDER_SESSION_NOT_FOUND");
        var linkedUid = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT CUSTOMER_UID FROM CUSTOMER_VISITS WHERE SESSION_ID=@Id;",
            new { Id = sessionId }, tx, cancellationToken: ct));
        if (linkedUid is not null)
        {
            if (existingUid is not null && !string.Equals(linkedUid, existingUid, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("此入場資料已有其他顧客 UID，請先核對歸戶。", "CUSTOMER_ALREADY_LINKED");
            var found = await c.QuerySingleAsync<CustomerProfileDto>(new CommandDefinition(
                $"SELECT {ProfileColumns} FROM CUSTOMER_PROFILES WHERE UID=@Uid;",
                new { Uid = linkedUid }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return found;
        }

        var uid = existingUid ?? newUid;
        if (existingUid is null)
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO CUSTOMER_PROFILES (UID,DISPLAY_NAME,CREDENTIAL_HASH,CREATED_AT,UPDATED_AT)
                VALUES (@Uid,@Name,NULL,NOW(6),NOW(6));
                """, new { Uid = uid, Name = name }, tx, cancellationToken: ct));
        }
        else if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM CUSTOMER_PROFILES WHERE UID=@Uid;",
            new { Uid = uid }, tx, cancellationToken: ct)) == 0)
        {
            throw new NotFoundException("找不到顧客。", "CUSTOMER_NOT_FOUND");
        }

        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO CUSTOMER_VISITS (SESSION_ID,CUSTOMER_UID,LINKED_AT,LINKED_BY)
            VALUES (@SessionId,@Uid,NOW(6),@ActorId);
            """, new { SessionId = sessionId, Uid = uid, ActorId = actorId }, tx, cancellationToken: ct));
        await Audit(c, tx, "customer", uid, existingUid is null ? "created_and_linked" : "visit_linked", actorId, ct);
        var profile = await c.QuerySingleAsync<CustomerProfileDto>(new CommandDefinition(
            $"SELECT {ProfileColumns} FROM CUSTOMER_PROFILES WHERE UID=@Uid;",
            new { Uid = uid }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return profile;
    }

    private const string VisitJoins = """
        FROM CUSTOMER_ORDER_SESSIONS S
        LEFT JOIN CUSTOMER_VISITS V ON V.SESSION_ID=S.ID
        LEFT JOIN (SELECT SESSION_ID,COUNT(*) AS OrderCount,
                          SUM(CASE WHEN ORDER_STATUS NOT IN ('cancelled','expired','rejected')
                                   THEN TOTAL_AMOUNT ELSE 0 END) AS OrderAmount
                   FROM ORDERS GROUP BY SESSION_ID) O ON O.SESSION_ID=S.ID
        LEFT JOIN (SELECT SESSION_ID,COUNT(*) AS CashCount,
                          SUM(CASE WHEN KIND='cash_receipt' THEN AMOUNT ELSE -AMOUNT END) AS NetReceived
                   FROM ORDERING_FINANCE_RECORDS WHERE KIND IN ('cash_receipt','cash_refund')
                   GROUP BY SESSION_ID) F ON F.SESSION_ID=S.ID
        LEFT JOIN (SELECT SESSION_ID,COUNT(*) AS PendingCount
                   FROM ART_DELIVERIES WHERE STATUS IN ('pending','in_progress','ready')
                   GROUP BY SESSION_ID) D ON D.SESSION_ID=S.ID
        """;

    private const string VisitColumns = """
        S.ID AS SessionId,V.CUSTOMER_UID AS CustomerUid,S.GAME_ID AS GameId,S.CUSTOMER_NAME AS CustomerName,
        S.BUSINESS_DATE AS BusinessDate,CAST(S.BUSINESS_PERIOD_ID AS CHAR(36)) AS BusinessPeriodId,S.SESSION_STATUS AS SessionStatus,
        S.ENTRY_STATUS AS EntryStatus,S.CREATED_AT AS CreatedAt,
        COALESCE(S.RECOVERY_CODE_ISSUED_AT,S.UPDATED_AT,S.CREATED_AT) AS RecoveryCodeIssuedAt,
        COALESCE(S.RECOVERY_CODE_VERSION,1) AS RecoveryCodeVersion,
        (S.RECOVERY_CODE_HASH IS NOT NULL) AS HasRecoveryCode,
        COALESCE(O.OrderCount,0) AS OrderCount,COALESCE(O.OrderAmount,0) AS OrderAmount,
        COALESCE(F.NetReceived,0) AS NetReceived,(COALESCE(F.CashCount,0)>0) AS HasCashRecords,
        COALESCE(D.PendingCount,0) AS PendingDeliveryCount,
        (SELECT COUNT(*) FROM CUSTOMER_ORDER_SESSIONS G
         WHERE UPPER(TRIM(G.GAME_ID))=UPPER(TRIM(S.GAME_ID))) AS GameIdVisitCount,
        (SELECT COALESCE(SUM(GO.TOTAL_AMOUNT),0)
         FROM CUSTOMER_ORDER_SESSIONS GS JOIN ORDERS GO ON GO.SESSION_ID=GS.ID
         WHERE UPPER(TRIM(GS.GAME_ID))=UPPER(TRIM(S.GAME_ID))
           AND GO.ORDER_STATUS NOT IN ('cancelled','expired','rejected')) AS GameIdOrderAmount
        """;

    public async Task<CustomerHistoryPage> History(
        DateOnly? businessDate, string? search, int page, int pageSize, string? uid, CancellationToken ct)
    {
        const string where = """
            WHERE (@Day IS NULL OR S.BUSINESS_DATE=@Day)
              AND (@Uid IS NULL OR V.CUSTOMER_UID=@Uid)
              AND (@Search IS NULL OR LOCATE(UPPER(@Search),UPPER(S.GAME_ID))>0
                   OR LOCATE(UPPER(@Search),UPPER(S.CUSTOMER_NAME))>0
                   OR LOCATE(UPPER(@Search),UPPER(V.CUSTOMER_UID))>0
                   OR S.ID=@Search)
            """;
        var args = new
        {
            Day = businessDate?.ToDateTime(TimeOnly.MinValue),
            Search = search,
            Uid = uid,
            Offset = (page - 1) * pageSize,
            Limit = pageSize
        };
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) {VisitJoins} {where};", args, cancellationToken: ct));
        var rows = (await c.QueryAsync<CustomerVisitDto>(new CommandDefinition(
            $"SELECT {VisitColumns} {VisitJoins} {where} ORDER BY S.BUSINESS_DATE DESC,S.CREATED_AT DESC,S.ID LIMIT @Limit OFFSET @Offset;",
            args, cancellationToken: ct))).AsList();
        if (rows.Count > 0)
        {
            var orders = await c.QueryAsync<HistoryOrderRow>(new CommandDefinition("""
                SELECT SESSION_ID AS SessionId,ID AS Id,ORDER_NUMBER AS OrderNumber,
                       ORDER_STATUS AS Status,TOTAL_AMOUNT AS TotalAmount
                FROM ORDERS WHERE SESSION_ID IN @Ids ORDER BY SUBMITTED_AT,ID;
                """, new { Ids = rows.Select(x => x.SessionId).ToArray() }, cancellationToken: ct));
            var map = orders.ToLookup(x => x.SessionId);
            foreach (var row in rows)
                row.Orders = map[row.SessionId]
                    .Select(x => new CustomerHistoryOrderDto(x.Id, x.OrderNumber, x.Status, x.TotalAmount))
                    .ToArray();
        }
        return new(page, pageSize, count, rows);
    }

    public async Task<CustomerSummaryDto> Summary(string uid, CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var row = await c.QuerySingleAsync<SummaryRow>(new CommandDefinition($"""
            SELECT COUNT(*) AS VisitCount,COALESCE(SUM(O.OrderCount),0) AS OrderCount,
                   COALESCE(SUM(O.OrderAmount),0) AS OrderAmount,
                   COALESCE(SUM(F.NetReceived),0) AS NetReceived,
                   COALESCE(SUM(D.PendingCount),0) AS PendingDeliveryCount
            {VisitJoins} WHERE V.CUSTOMER_UID=@Uid;
            """, new { Uid = uid }, cancellationToken: ct));
        return new(row.VisitCount, row.OrderCount, row.OrderAmount, row.NetReceived, row.PendingDeliveryCount);
    }

    private static Task<int> Audit(
        MySqlConnection c, MySqlTransaction tx, string type, string id, string action, string? actorId, CancellationToken ct)
        => c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO CUSTOMER_DELIVERY_AUDIT (ID,ENTITY_TYPE,ENTITY_ID,ACTION,ACTOR_ID,CREATED_AT)
            VALUES (@Id,@Type,@EntityId,@Action,@ActorId,NOW(6));
            """, new { Id = Guid.NewGuid().ToString(), Type = type, EntityId = id, Action = action, ActorId = actorId },
            tx, cancellationToken: ct));

    private sealed class HistoryOrderRow
    {
        public string SessionId { get; set; } = "";
        public string Id { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public long TotalAmount { get; set; }
    }

    private sealed class SummaryRow
    {
        public int VisitCount { get; set; }
        public int OrderCount { get; set; }
        public long OrderAmount { get; set; }
        public long NetReceived { get; set; }
        public int PendingDeliveryCount { get; set; }
    }
}
