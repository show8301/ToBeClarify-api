using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingFinanceRepository
{
    private const string TipOrderSql = """
        SELECT CAST(O.ID AS CHAR(36)) AS OrderId, O.ORDER_NUMBER AS OrderNumber,
            CAST(T.ORDER_ITEM_ID AS CHAR(36)) AS ItemId, T.TIP_AMOUNT AS Amount,
            COALESCE(D.VERSION,0) AS Version, D.SNAPSHOT_JSON AS SnapshotJson,
            T.STAFF_NAME_SNAPSHOT AS StaffName, T.STAFF_AMOUNT AS StaffAmount, T.STORE_AMOUNT AS StoreAmount,
            CAST(T.STAFF_ID AS CHAR(36)) AS StaffId, T.STAFF_PERCENTAGE AS StaffPercentage, T.STORE_PERCENTAGE AS StorePercentage
        FROM ORDER_TIPS T JOIN ORDERS O ON O.ID=T.ORDER_ID
        LEFT JOIN ORDER_TIP_DISTRIBUTIONS D ON D.ORDER_ITEM_ID=T.ORDER_ITEM_ID
        WHERE O.SESSION_ID=@SessionId AND O.ORDER_STATUS NOT IN ('cancelled','expired','rejected')
        """;

    public async Task<IReadOnlyList<OrderTipDistributionDto>> GetOrderTipDistributionsAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<TipOrderRow>(new CommandDefinition(TipOrderSql + " ORDER BY O.ORDER_NUMBER,T.ORDER_ITEM_ID;",
            new { SessionId = sessionId }, cancellationToken: ct));
        return rows.Select(x => new OrderTipDistributionDto(x.OrderId, x.OrderNumber, x.ItemId, x.Amount, x.Version,
            x.SnapshotJson is null ? null : JsonSerializer.Deserialize<OrderTipDistributionSnapshot>(x.SnapshotJson),
            x.StaffName, x.StaffAmount, x.StoreAmount)).ToArray();
    }

    internal static Task LockTipSettlementAsync(MySqlConnection connection, MySqlTransaction tx, CancellationToken ct)
        => connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT ID FROM ORDERING_RUNTIME_LOCKS WHERE ID='operating_period' FOR UPDATE;", transaction: tx, cancellationToken: ct));

    internal static Task InvalidateTipDraftAsync(MySqlConnection connection, MySqlTransaction tx, string sessionId, CancellationToken ct)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE SETTLEMENT_RUNS R JOIN CUSTOMER_ORDER_SESSIONS S ON S.BUSINESS_DATE=R.BUSINESS_DATE
            SET R.INPUT_VERSION=R.INPUT_VERSION+1,R.STATUS='draft'
            WHERE S.ID=@SessionId AND R.STATUS<>'finalized';
            """, new { SessionId = sessionId }, tx, cancellationToken: ct));

    private static async Task<OrderTipDistributionSnapshot> ResolveTipDistributionAsync(MySqlConnection connection,
        MySqlTransaction tx, string periodId, int amount, string mode,
        IReadOnlyList<SaveOrderingFinanceTipAllocationRequest> allocations, CancellationToken ct)
    {
        var staff = await GetEligibleTipStaffAsync(connection, tx, periodId, ct);
        return OrderTipDistributionPolicy.Calculate(amount, mode, allocations, staff.GroupBy(x => x.StaffId)
            .Select(g => new OrderingFinanceTipStaffOptionDto(g.Key, g.First().DisplayName, g.Select(x => x.Role).Distinct().ToArray())).ToArray());
    }

    internal static async Task SaveNewTipDistributionsAsync(MySqlConnection connection, MySqlTransaction tx,
        NewOrderAggregate order, CancellationToken ct)
    {
        foreach (var tip in order.Tips.Where(x => x.Distribution is not null))
        {
            var periodId = await connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT CAST(BUSINESS_PERIOD_ID AS CHAR(36)) FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId;",
                new { order.SessionId }, tx, cancellationToken: ct));
            var expected = tip.Distribution!;
            // Standard customer tips already resolve the named staff server-side. Their remainder is public.
            // This aggregate is internal; correction requests still pass the attendance/percentage policy.
            var distribution = order.Backfill is null && expected.Mode == "custom"
                ? expected : await ResolveTipDistributionAsync(connection, tx, periodId, tip.Amount, expected.Mode,
                    expected.Allocations.Select(x => new SaveOrderingFinanceTipAllocationRequest(x.StaffId, x.Percentage)).ToArray(), ct);
            if (distribution.Mode != "public" && distribution.Allocations.Sum(x => x.Amount) + distribution.PublicAmount != tip.Amount)
                throw new BusinessException("小費分配總額與訂單不符。", "TIP_DISTRIBUTION_INVALID");
            if (JsonSerializer.Serialize(distribution) != JsonSerializer.Serialize(expected))
                throw new ConflictException("小費分配已變更，請重新取得報價。", "TIP_DISTRIBUTION_CHANGED");
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ORDER_TIP_DISTRIBUTIONS (ORDER_ITEM_ID,VERSION,SNAPSHOT_JSON,UPDATED_AT,UPDATED_BY)
                VALUES (@ItemId,1,@Snapshot,@Now,@ActorId);
                """, new { ItemId = tip.OrderItemId, Snapshot = JsonSerializer.Serialize(distribution),
                Now = order.SubmittedAt, order.ActorId }, tx, cancellationToken: ct));
        }
    }

    public async Task<OrderingFinanceOperationDto> CorrectOrderTipAsync(string sessionId, string itemId,
        CorrectOrderTipDistributionRequest request, string hash, string actorId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await LockTipSettlementAsync(connection, tx, ct);
        var session = await GetSessionAsync(connection, tx, sessionId, true, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition(
            "SELECT SESSION_ID AS SessionId,PAYLOAD_HASH AS PayloadHash,RESULT_JSON AS ResultJson FROM ORDERING_FINANCE_OPERATIONS WHERE OPERATION_ID=@OperationId;",
            new { request.OperationId }, tx, cancellationToken: ct));
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.PayloadHash != hash)
                throw new ConflictException("操作編號已用於不同內容。", "FINANCE_OPERATION_CONFLICT");
            return JsonSerializer.Deserialize<OrderingFinanceOperationDto>(prior.ResultJson)!;
        }
        if (session.FlowVersion < 2 || session.SourcePeriodId is null)
            throw new BusinessException("此顧客不支援分項帳款更正。", "FINANCE_LEGACY_READ_ONLY");
        var version = await EnsureAccountAsync(connection, tx, session, ct);
        if (version != request.ExpectedAccountVersion)
            throw new ConflictException("帳款已更新，請重新讀取。", "FINANCE_VERSION_CONFLICT");
        var row = await connection.QuerySingleOrDefaultAsync<TipOrderRow>(new CommandDefinition(
            TipOrderSql + " AND T.ORDER_ITEM_ID=@ItemId FOR UPDATE;", new { SessionId = sessionId, ItemId = itemId }, tx, cancellationToken: ct));
        if (row is null || row.Version != request.ExpectedDistributionVersion)
            throw new ConflictException("小費訂單或分配已更新，請重新讀取。", "TIP_DISTRIBUTION_CHANGED");
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT COUNT(*)>0 FROM ORDERING_FINANCE_RECORDS
            WHERE ORDER_ID=@OrderId AND KIND='tip_supplement' AND VOIDED_AT IS NULL;
            """, new { row.OrderId }, tx, cancellationToken: ct)))
            throw new ConflictException("此訂單仍有舊版補登小費，請先核對並作廢重複分配。", "TIP_LEGACY_RECONCILIATION_REQUIRED");
        var distribution = await ResolveTipDistributionAsync(connection, tx, session.SourcePeriodId,
            row.Amount, request.Mode, request.Allocations, ct);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var result = new OrderingFinanceOperationDto(request.OperationId, itemId, version + 1);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDER_TIP_DISTRIBUTIONS (ORDER_ITEM_ID,VERSION,SNAPSHOT_JSON,UPDATED_AT,UPDATED_BY)
            VALUES (@ItemId,@Version,@Snapshot,@Now,@ActorId)
            ON DUPLICATE KEY UPDATE VERSION=VALUES(VERSION),SNAPSHOT_JSON=VALUES(SNAPSHOT_JSON),UPDATED_AT=VALUES(UPDATED_AT),UPDATED_BY=VALUES(UPDATED_BY);
            UPDATE ORDER_ITEMS SET NAME_SNAPSHOT=@Label,REFERENCE_ID=NULL,UPDATED_AT=@Now,UPDATED_BY=@ActorId WHERE ID=@ItemId;
            UPDATE ORDER_TIPS SET STAFF_ID=NULL,STAFF_NAME_SNAPSHOT=@Label,STAFF_PERCENTAGE=100,
                STORE_PERCENTAGE=0,STAFF_AMOUNT=TIP_AMOUNT,STORE_AMOUNT=0,UPDATED_AT=@Now WHERE ORDER_ITEM_ID=@ItemId;
            UPDATE ORDERING_FINANCE_ACCOUNTS SET VERSION=VERSION+1,UPDATED_AT=@Now WHERE SESSION_ID=@SessionId;
            INSERT INTO ORDER_AUDIT_LOG (ID,ORDER_ID,SESSION_ID,ACTION_TYPE,BEFORE_JSON,AFTER_JSON,ACTOR_ID,ACTOR_ROLE,CREATED_AT)
            VALUES (UUID(),@OrderId,@SessionId,'order.tip_distribution.corrected',@Before,@After,@ActorId,'staff',@Now);
            INSERT INTO ORDERING_FINANCE_OPERATIONS (OPERATION_ID,SESSION_ID,PAYLOAD_HASH,RESULT_JSON,CREATED_AT,CREATED_BY)
            VALUES (@OperationId,@SessionId,@Hash,@Result,@Now,@ActorId);
            """, new { ItemId = itemId, Version = row.Version + 1, Snapshot = JsonSerializer.Serialize(distribution),
            Label = distribution.Mode == "public" ? "公共小費（按工時分配）" : distribution.Mode == "custom" ? "小費｜自訂比例" : "小費｜指定店員",
            Now = now, ActorId = actorId, SessionId = sessionId, row.OrderId, Before = JsonSerializer.Serialize(row),
            After = JsonSerializer.Serialize(new { distribution, request.Reason }), request.OperationId, Hash = hash,
            Result = JsonSerializer.Serialize(result) }, tx, cancellationToken: ct));
        await InvalidateTipDraftAsync(connection, tx, sessionId, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private sealed class TipOrderRow
    {
        public string OrderId { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string ItemId { get; set; } = "";
        public int Amount { get; set; }
        public int Version { get; set; }
        public string? SnapshotJson { get; set; }
        public string? StaffId { get; set; }
        public int StaffPercentage { get; set; }
        public int StorePercentage { get; set; }
        public string? StaffName { get; set; }
        public int StaffAmount { get; set; }
        public int StoreAmount { get; set; }
    }
}
