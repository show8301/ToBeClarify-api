using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed class OrderingFinanceRepository(AppDbContext dbContext)
{
    private const string SessionSql = """
        SELECT s.ID AS SessionId, s.BUSINESS_DATE AS BusinessDate,
               s.BUSINESS_PERIOD_ID AS SourcePeriodId, COALESCE(p.FLOW_VERSION, 1) AS FlowVersion
        FROM CUSTOMER_ORDER_SESSIONS s LEFT JOIN BUSINESS_PERIODS p ON p.ID = s.BUSINESS_PERIOD_ID
        WHERE s.ID = @SessionId
        """;
    private const string RecordsSql = """
        SELECT ID AS Id, VERSION AS Version, KIND AS Kind, AMOUNT AS Amount,
               SOURCE_KIND AS SourceKind, ORDER_ID AS OrderId, ORDER_ITEM_ID AS OrderItemId,
               SOURCE_PERIOD_ID AS SourcePeriodId, CASH_PERIOD_ID AS CashPeriodId,
               OCCURRED_AT AS OccurredAt, ALLOCATION_STATUS AS AllocationStatus, HOLD_SCOPE AS HoldScope,
               REASON AS Reason, REVERSES_RECORD_ID AS ReversesRecordId,
               CREATED_AT AS CreatedAt, CREATED_BY AS CreatedBy,
               UPDATED_AT AS UpdatedAt, UPDATED_BY AS UpdatedBy,
               CONFIRMED_AT AS ConfirmedAt, CONFIRMED_BY AS ConfirmedBy
        FROM ORDERING_FINANCE_RECORDS WHERE SESSION_ID = @SessionId
        """;

    public async Task<OrderingFinanceAccountDto> GetAccountAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var session = await GetSessionAsync(connection, transaction, sessionId, false, ct);
        var version = await connection.QuerySingleOrDefaultAsync<long>(new CommandDefinition(
            "SELECT VERSION FROM ORDERING_FINANCE_ACCOUNTS WHERE SESSION_ID = @SessionId;",
            new { SessionId = sessionId }, transaction, cancellationToken: ct));
        var records = (await connection.QueryAsync<RecordRow>(new CommandDefinition(
            RecordsSql + " ORDER BY CREATED_AT, ID;", new { SessionId = sessionId }, transaction, cancellationToken: ct)))
            .Select(Map).ToArray();
        // Fulfillment cancellation adjusts these totals and returns meal credit once.
        // Never add a second charge reduction for that same cancellation here.
        var orderAmount = await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT COALESCE(SUM(TOTAL_AMOUNT), 0) FROM ORDERS
            WHERE SESSION_ID = @SessionId AND ORDER_STATUS NOT IN ('cancelled', 'expired', 'rejected');
            """, new { SessionId = sessionId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        var additions = records.Where(x => x.Kind == "charge_add").Sum(x => x.Amount);
        var reductions = records.Where(x => x.Kind == "charge_reduce").Sum(x => x.Amount);
        var received = records.Where(x => x.Kind == "cash_receipt").Sum(x => x.Amount);
        var refunded = records.Where(x => x.Kind == "cash_refund").Sum(x => x.Amount);
        var receivable = orderAmount + additions - reductions;
        var net = received - refunded;
        var pending = records.Count(x => x.AllocationStatus == "pending" ||
            x.Kind is "cash_receipt" or "cash_refund" && x.CashPeriodId is null);
        var refundDue = Math.Max(0, net - receivable);
        var unassigned = records.Where(x => x.CashPeriodId is null)
            .Sum(x => x.Kind == "cash_receipt" ? x.Amount : x.Kind == "cash_refund" ? -x.Amount : 0);
        var hasCash = records.Any(x => x.Kind is "cash_receipt" or "cash_refund");
        var admissionRecorded = records.Any(x => x.SourceKind == "admission" && x.Kind == "charge_add");
        return new OrderingFinanceAccountDto(sessionId, session.SourcePeriodId,
            session.BusinessDate.ToString("yyyy-MM-dd"), session.FlowVersion, version,
            session.FlowVersion >= 2 && session.SourcePeriodId is not null, hasCash, admissionRecorded,
            orderAmount, additions, reductions, receivable, received, refunded, net, receivable - net,
            refundDue, unassigned, pending, pending > 0 || !admissionRecorded || session.FlowVersion < 2,
            pending > 0 || refundDue > 0 || receivable > net || records.Any(x => x.HoldScope != "none")
                ? "session" : "none", records);
    }

    public async Task<OrderingFinanceOperationDto?> GetOperationAsync(string sessionId, string operationId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var json = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT RESULT_JSON FROM ORDERING_FINANCE_OPERATIONS
            WHERE SESSION_ID = @SessionId AND OPERATION_ID = @OperationId;
            """, new { SessionId = sessionId, OperationId = operationId }, cancellationToken: ct));
        return json is null ? null : JsonSerializer.Deserialize<OrderingFinanceOperationDto>(json);
    }

    public async Task<IReadOnlyList<OrderingFinancePeriodOptionDto>> GetPeriodOptionsAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<PeriodOptionRow>(new CommandDefinition("""
            SELECT p.ID AS Id, p.BUSINESS_DATE AS BusinessDate FROM BUSINESS_PERIODS p
            WHERE EXISTS (SELECT 1 FROM CUSTOMER_ORDER_SESSIONS WHERE ID = @SessionId)
            ORDER BY p.BUSINESS_DATE DESC LIMIT 200;
            """, new { SessionId = sessionId }, cancellationToken: ct));
        return rows.Select(x => new OrderingFinancePeriodOptionDto(x.Id, x.BusinessDate.ToString("yyyy-MM-dd"))).ToArray();
    }

    public async Task<IReadOnlyList<OrderingFinanceRevisionDto>> GetRevisionsAsync(string sessionId, string recordId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<RevisionRow>(new CommandDefinition("""
            SELECT v.VERSION AS Version, v.OPERATION_ID AS OperationId, v.SNAPSHOT_JSON AS SnapshotJson,
                   v.RECORDED_AT AS RecordedAt, v.RECORDED_BY AS RecordedBy
            FROM ORDERING_FINANCE_REVISIONS v JOIN ORDERING_FINANCE_RECORDS r ON r.ID = v.RECORD_ID
            WHERE r.SESSION_ID = @SessionId AND r.ID = @RecordId ORDER BY v.VERSION;
            """, new { SessionId = sessionId, RecordId = recordId }, cancellationToken: ct));
        return rows.Select(x => new OrderingFinanceRevisionDto(x.Version, x.OperationId, Offset(x.RecordedAt),
            x.RecordedBy, JsonSerializer.Deserialize<OrderingFinanceRecordDto>(x.SnapshotJson)
                ?? throw new InvalidOperationException("Invalid finance revision snapshot."))).ToArray();
    }

    public async Task<OrderingFinanceOperationDto> SaveAsync(string sessionId, string? recordId,
        SaveOrderingFinanceRecordRequest request, string payloadHash, string actorId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var session = await GetSessionAsync(connection, transaction, sessionId, true, ct);
        // Inspect the operation before checking the current version: an exact retry of a
        // committed event must succeed even if another clerk has since changed the account.
        var prior = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition("""
            SELECT SESSION_ID AS SessionId, PAYLOAD_HASH AS PayloadHash, RESULT_JSON AS ResultJson
            FROM ORDERING_FINANCE_OPERATIONS WHERE OPERATION_ID = @OperationId;
            """, new { request.OperationId }, transaction, cancellationToken: ct));
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.PayloadHash != payloadHash)
                throw new BusinessException("相同操作識別碼已用於不同內容，請先查回原操作。", "FINANCE_OPERATION_CONFLICT");
            return JsonSerializer.Deserialize<OrderingFinanceOperationDto>(prior.ResultJson)
                ?? throw new InvalidOperationException("Invalid finance operation snapshot.");
        }
        if (session.FlowVersion < 2 || session.SourcePeriodId is null)
            throw new BusinessException("此顧客屬既有營業期，沿用原帳務；新費用紀錄於新版營業期啟用。", "FINANCE_LEGACY_READ_ONLY");
        if (request.SourcePeriodId is not null && request.SourcePeriodId != session.SourcePeriodId)
            throw new BusinessException("原業務營業期必須與顧客帳單一致。", "FINANCE_SOURCE_PERIOD_MISMATCH");
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_ACCOUNTS (SESSION_ID, SOURCE_PERIOD_ID, VERSION, UPDATED_AT)
            VALUES (@SessionId, @SourcePeriodId, 0, @Now)
            ON DUPLICATE KEY UPDATE SESSION_ID = VALUES(SESSION_ID);
            """, new { SessionId = sessionId, session.SourcePeriodId, Now = now }, transaction, cancellationToken: ct));
        var version = await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT VERSION FROM ORDERING_FINANCE_ACCOUNTS WHERE SESSION_ID = @SessionId FOR UPDATE;
            """, new { SessionId = sessionId }, transaction, cancellationToken: ct));
        if (version != request.ExpectedVersion)
            throw new BusinessException("帳款已被其他操作更新，請重新讀取後確認輸入。", "FINANCE_VERSION_CONFLICT");
        await ValidateSourceAsync(connection, transaction, sessionId, request, ct);
        var cashPeriodId = request.CashPeriodId;
        if (cashPeriodId is not null)
        {
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM BUSINESS_PERIODS WHERE ID = @Id;", new { Id = cashPeriodId },
                transaction, cancellationToken: ct));
            if (exists != 1)
                throw new BusinessException("指定的收退款營業期不存在，可先留待歸屬保存。", "FINANCE_CASH_PERIOD_NOT_FOUND");
        }
        RecordRow? original = null;
        if (recordId is not null)
        {
            original = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(
                RecordsSql + " AND ID = @RecordId FOR UPDATE;", new { SessionId = sessionId, RecordId = recordId },
                transaction, cancellationToken: ct));
            if (original is null)
                throw new BusinessException("找不到這筆費用紀錄。", "FINANCE_RECORD_NOT_FOUND");
            if (original.Kind != request.Kind || original.ReversesRecordId != request.ReversesRecordId)
                throw new BusinessException("更正保留原事件種類；真的收到或退回款項請另外登記。", "FINANCE_EVENT_KIND_IMMUTABLE");
        }
        if (request.ReversesRecordId is not null)
        {
            var reverse = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(
                RecordsSql + " AND ID = @RecordId;", new { SessionId = sessionId, RecordId = request.ReversesRecordId },
                transaction, cancellationToken: ct));
            var opposite = request.Kind == "cash_refund" ? "cash_receipt" : "cash_refund";
            if (reverse is null || reverse.Kind != opposite || reverse.Id == recordId)
                throw new BusinessException("反向款項須關聯同一顧客的原實收／實退。", "FINANCE_INVALID_REVERSAL");
        }
        var row = new RecordRow
        {
            Id = recordId ?? Guid.NewGuid().ToString(), SessionId = sessionId,
            Version = (original?.Version ?? 0) + 1, Kind = request.Kind, Amount = request.Amount,
            SourceKind = request.SourceKind, OrderId = request.OrderId, OrderItemId = request.OrderItemId,
            SourcePeriodId = session.SourcePeriodId, CashPeriodId = cashPeriodId,
            OccurredAt = request.OccurredAt.ToOffset(TimeSpan.FromHours(8)).DateTime,
            AllocationStatus = request.AllocationStatus, HoldScope = request.HoldScope, Reason = request.Reason,
            ReversesRecordId = request.ReversesRecordId,
            CreatedAt = original?.CreatedAt ?? now, CreatedBy = original?.CreatedBy ?? actorId,
            UpdatedAt = now, UpdatedBy = actorId,
            ConfirmedAt = request.AllocationStatus == "confirmed" ? now : null,
            ConfirmedBy = request.AllocationStatus == "confirmed" ? actorId : null
        };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_RECORDS
                (ID, SESSION_ID, VERSION, KIND, AMOUNT, SOURCE_KIND, ORDER_ID, ORDER_ITEM_ID,
                 SOURCE_PERIOD_ID, CASH_PERIOD_ID, OCCURRED_AT, ALLOCATION_STATUS, HOLD_SCOPE,
                 REASON, REVERSES_RECORD_ID, CREATED_AT, CREATED_BY, UPDATED_AT, UPDATED_BY, CONFIRMED_AT, CONFIRMED_BY)
            VALUES (@Id, @SessionId, @Version, @Kind, @Amount, @SourceKind, @OrderId, @OrderItemId,
                    @SourcePeriodId, @CashPeriodId, @OccurredAt, @AllocationStatus, @HoldScope,
                    @Reason, @ReversesRecordId, @CreatedAt, @CreatedBy, @UpdatedAt, @UpdatedBy, @ConfirmedAt, @ConfirmedBy)
            ON DUPLICATE KEY UPDATE VERSION = VALUES(VERSION), AMOUNT = VALUES(AMOUNT),
                SOURCE_KIND = VALUES(SOURCE_KIND), ORDER_ID = VALUES(ORDER_ID), ORDER_ITEM_ID = VALUES(ORDER_ITEM_ID),
                CASH_PERIOD_ID = VALUES(CASH_PERIOD_ID), OCCURRED_AT = VALUES(OCCURRED_AT),
                ALLOCATION_STATUS = VALUES(ALLOCATION_STATUS), HOLD_SCOPE = VALUES(HOLD_SCOPE),
                REASON = VALUES(REASON), UPDATED_AT = VALUES(UPDATED_AT), UPDATED_BY = VALUES(UPDATED_BY),
                CONFIRMED_AT = VALUES(CONFIRMED_AT), CONFIRMED_BY = VALUES(CONFIRMED_BY);
            """, row, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_REVISIONS
                (RECORD_ID, VERSION, OPERATION_ID, SNAPSHOT_JSON, RECORDED_AT, RECORDED_BY)
            VALUES (@Id, @Version, @OperationId, @Snapshot, @Now, @ActorId);
            UPDATE ORDERING_FINANCE_ACCOUNTS SET VERSION = VERSION + 1, UPDATED_AT = @Now
            WHERE SESSION_ID = @SessionId;
            """, new
        {
            row.Id, row.Version, request.OperationId, Snapshot = JsonSerializer.Serialize(Map(row)),
            Now = now, ActorId = actorId, SessionId = sessionId
        }, transaction, cancellationToken: ct));
        var result = new OrderingFinanceOperationDto(request.OperationId, row.Id, version + 1);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_OPERATIONS
                (OPERATION_ID, SESSION_ID, PAYLOAD_HASH, RESULT_JSON, CREATED_AT, CREATED_BY)
            VALUES (@OperationId, @SessionId, @PayloadHash, @ResultJson, @Now, @ActorId);
            """, new
        {
            request.OperationId, SessionId = sessionId, PayloadHash = payloadHash,
            ResultJson = JsonSerializer.Serialize(result), Now = now, ActorId = actorId
        }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<SessionRow> GetSessionAsync(MySqlConnection connection, MySqlTransaction transaction,
        string sessionId, bool forUpdate, CancellationToken ct)
        => await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition(
            SessionSql + (forUpdate ? " FOR UPDATE;" : ";"), new { SessionId = sessionId }, transaction, cancellationToken: ct))
            ?? throw new BusinessException("找不到顧客帳單。", "FINANCE_SESSION_NOT_FOUND");

    private static async Task ValidateSourceAsync(MySqlConnection connection, MySqlTransaction transaction,
        string sessionId, SaveOrderingFinanceRecordRequest request, CancellationToken ct)
    {
        if (request.OrderId is null) return;
        var orderId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT ID FROM ORDERS WHERE ID = @OrderId AND SESSION_ID = @SessionId FOR UPDATE;
            """, new { request.OrderId, SessionId = sessionId }, transaction, cancellationToken: ct));
        if (orderId is null)
            throw new BusinessException("原訂單不屬於這位顧客。", "FINANCE_ORDER_MISMATCH");
        if (request.OrderItemId is null) return;
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM ORDER_ITEMS WHERE ID = @OrderItemId AND ORDER_ID = @OrderId;
            """, new { request.OrderId, request.OrderItemId }, transaction, cancellationToken: ct));
        if (exists != 1)
            throw new BusinessException("費用項目不屬於指定原訂單。", "FINANCE_ITEM_MISMATCH");
    }

    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
    private static OrderingFinanceRecordDto Map(RecordRow x) => new(x.Id, x.Version, x.Kind, x.Amount,
        x.SourceKind, x.OrderId, x.OrderItemId, x.SourcePeriodId, x.CashPeriodId, Offset(x.OccurredAt),
        x.AllocationStatus, x.HoldScope, x.Reason, x.ReversesRecordId, Offset(x.CreatedAt), x.CreatedBy,
        Offset(x.UpdatedAt), x.UpdatedBy, x.ConfirmedAt is { } confirmed ? Offset(confirmed) : null, x.ConfirmedBy);

    private sealed class SessionRow
    {
        public string SessionId { get; set; } = "";
        public DateTime BusinessDate { get; set; }
        public string? SourcePeriodId { get; set; }
        public int FlowVersion { get; set; }
    }
    private sealed class PeriodOptionRow
    {
        public string Id { get; set; } = "";
        public DateTime BusinessDate { get; set; }
    }
    private sealed class OperationRow
    {
        public string SessionId { get; set; } = "";
        public string PayloadHash { get; set; } = "";
        public string ResultJson { get; set; } = "";
    }
    private sealed class RevisionRow
    {
        public long Version { get; set; }
        public string OperationId { get; set; } = "";
        public string SnapshotJson { get; set; } = "";
        public DateTime RecordedAt { get; set; }
        public string RecordedBy { get; set; } = "";
    }
    private sealed class RecordRow
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public long Version { get; set; }
        public string Kind { get; set; } = "";
        public long Amount { get; set; }
        public string SourceKind { get; set; } = "";
        public string? OrderId { get; set; }
        public string? OrderItemId { get; set; }
        public string SourcePeriodId { get; set; } = "";
        public string? CashPeriodId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string AllocationStatus { get; set; } = "";
        public string HoldScope { get; set; } = "";
        public string Reason { get; set; } = "";
        public string? ReversesRecordId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime? ConfirmedAt { get; set; }
        public string? ConfirmedBy { get; set; }
    }
}
