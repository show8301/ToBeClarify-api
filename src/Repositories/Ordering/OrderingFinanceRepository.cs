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
               s.BUSINESS_PERIOD_ID AS SourcePeriodId, COALESCE(p.FLOW_VERSION, 1) AS FlowVersion,
               COALESCE(s.ENTRY_STATUS, 'open') AS EntryStatus, s.DEPARTED_AT AS DepartedAt,
               s.DEPARTURE_REASON AS DepartureReason, s.SESSION_STATUS AS SessionStatus
        FROM CUSTOMER_ORDER_SESSIONS s LEFT JOIN BUSINESS_PERIODS p ON p.ID = s.BUSINESS_PERIOD_ID
        WHERE s.ID = @SessionId
        """;
    private const string RecordsSql = """
        SELECT ID AS Id, VERSION AS Version, KIND AS Kind, AMOUNT AS Amount,
               SOURCE_KIND AS SourceKind, ORDER_ID AS OrderId, ORDER_ITEM_ID AS OrderItemId,
               SOURCE_PERIOD_ID AS SourcePeriodId, CASH_PERIOD_ID AS CashPeriodId,
               OCCURRED_AT AS OccurredAt, ALLOCATION_STATUS AS AllocationStatus, HOLD_SCOPE AS HoldScope,
               REASON AS Reason, REVERSES_RECORD_ID AS ReversesRecordId, CASE_ID AS CaseId,
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
        var admissionRecorded = records.Any(x => x.SourceKind == "admission" && x.Kind == "charge_add") ||
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM ORDERING_SESSION_ADMISSIONS WHERE SESSION_ID=@SessionId AND STATUS <> 'reissue';",
                new { SessionId = sessionId }, cancellationToken: ct)) > 0;
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

    public async Task<OrderingAdmissionDto?> GetAdmissionAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<AdmissionRow>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, AMOUNT AS Amount, DISCOUNT_AMOUNT AS DiscountAmount,
                   CREDIT_AMOUNT AS CreditAmount, STATUS AS Status, CASH_PERIOD_ID AS CashPeriodId,
                   CHARGE_RECORD_ID AS ChargeRecordId, RECEIPT_RECORD_ID AS ReceiptRecordId, VERSION AS Version,
                   REASON AS Reason, CREATED_AT AS CreatedAt, UPDATED_AT AS UpdatedAt
            FROM ORDERING_SESSION_ADMISSIONS WHERE SESSION_ID=@SessionId;
            """, new { SessionId = sessionId }, cancellationToken: ct));
        return row is null ? null : MapAdmission(row);
    }

    public async Task<IReadOnlyList<OrderingFinanceCaseDto>> GetCasesAsync(string sessionId, bool includeResolved, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<CaseRow>(new CommandDefinition($"""
            SELECT ID AS Id, SESSION_ID AS SessionId, RECORD_ID AS RecordId, ORDER_ID AS OrderId,
                   ORDER_ITEM_ID AS OrderItemId, CASE_KIND AS CaseKind, AMOUNT AS Amount,
                   PROFIT_SCOPE AS ProfitScope, STATUS AS Status, SOURCE_PERIOD_ID AS SourcePeriodId,
                   DESCRIPTION AS Description, CREATED_AT AS CreatedAt, CREATED_BY AS CreatedBy,
                   UPDATED_AT AS UpdatedAt, UPDATED_BY AS UpdatedBy, RESOLVED_AT AS ResolvedAt,
                   RESOLVED_BY AS ResolvedBy, RESOLUTION_NOTE AS ResolutionNote
            FROM ORDERING_FINANCE_CASES WHERE SESSION_ID=@SessionId {(includeResolved ? "" : "AND STATUS='open'")}
            ORDER BY CREATED_AT, ID;
            """, new { SessionId = sessionId }, cancellationToken: ct));
        return rows.Select(MapCase).ToArray();
    }

    public async Task<OrderingCustomerBillDto> GetCustomerBillAsync(string sessionId, CancellationToken ct)
    {
        var account = await GetAccountAsync(sessionId, ct);
        var admission = await GetAdmissionAsync(sessionId, ct);
        var records = account.Records.Select(x => new OrderingCustomerFinanceRecordDto(
            x.Kind, x.Amount, x.SourceKind, x.OrderId, x.OrderItemId, x.OccurredAt,
            x.AllocationStatus, x.Reason, x.CaseId)).ToArray();
        return new OrderingCustomerBillDto(sessionId, admission?.Amount ?? 0, admission?.DiscountAmount ?? 0,
            admission?.CreditAmount ?? 0, admission?.Status ?? "unrecorded", account.OrderReceivable,
            account.ChargeAdditions, account.ChargeReductions, account.Receivable, account.CashReceived,
            account.CashRefunded, account.NetCash, account.Balance, account.RefundDue, account.PendingCount,
            account.Records.Any(x => x.CaseId is not null && x.AllocationStatus == "pending"), records);
    }

    public async Task<OrderingAdmissionOperationDto> SaveAdmissionAsync(string sessionId,
        SaveOrderingAdmissionRequest request, string payloadHash, string actorId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var session = await GetSessionAsync(connection, transaction, sessionId, true, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition("""
            SELECT SESSION_ID AS SessionId, PAYLOAD_HASH AS PayloadHash, RESULT_JSON AS ResultJson
            FROM ORDERING_FINANCE_OPERATIONS WHERE OPERATION_ID=@OperationId;
            """, new { request.OperationId }, transaction, cancellationToken: ct));
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.PayloadHash != payloadHash)
                throw new BusinessException("相同操作識別碼已用於不同內容。", "FINANCE_OPERATION_CONFLICT");
            return JsonSerializer.Deserialize<OrderingAdmissionOperationDto>(prior.ResultJson)
                ?? throw new InvalidOperationException("Invalid admission operation snapshot.");
        }
        if (session.FlowVersion < 2 || session.SourcePeriodId is null)
            throw new BusinessException("此營業期尚未啟用新版入場帳。", "FINANCE_LEGACY_READ_ONLY");
        var existing = await connection.QuerySingleOrDefaultAsync<AdmissionRow>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, AMOUNT AS Amount, DISCOUNT_AMOUNT AS DiscountAmount,
                   CREDIT_AMOUNT AS CreditAmount, STATUS AS Status, CASH_PERIOD_ID AS CashPeriodId,
                   CHARGE_RECORD_ID AS ChargeRecordId, RECEIPT_RECORD_ID AS ReceiptRecordId, VERSION AS Version,
                   REASON AS Reason, CREATED_AT AS CreatedAt, UPDATED_AT AS UpdatedAt
            FROM ORDERING_SESSION_ADMISSIONS WHERE SESSION_ID=@SessionId FOR UPDATE;
            """, new { SessionId = sessionId }, transaction, cancellationToken: ct));
        if (existing is not null && request.Mode is not "reissue")
            throw new BusinessException("此顧客已有入場事件；如需改正請更正原紀錄，不能再次收取入場費。", "ADMISSION_ALREADY_RECORDED");
        var accountVersion = await EnsureAccountAsync(connection, transaction, session, ct);
        if (accountVersion != request.ExpectedVersion)
            throw new BusinessException("帳款已被其他操作更新，請重新讀取。", "FINANCE_VERSION_CONFLICT");
        if (request.Amount < 0 || request.DiscountAmount < 0 || request.DiscountAmount > request.Amount || request.CreditAmount < 0)
            throw new BusinessException("入場費、折讓與折抵額度無效。", "ADMISSION_INVALID_AMOUNT");
        if (request.CashPeriodId is not null && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM BUSINESS_PERIODS WHERE ID=@Id;", new { Id=request.CashPeriodId }, transaction, cancellationToken:ct)) != 1)
            throw new BusinessException("指定的收款營業期不存在。", "FINANCE_CASH_PERIOD_NOT_FOUND");
        if (request.Mode is not ("received" or "unpaid" or "waived" or "reissue"))
            throw new BusinessException("入場狀態無效。", "ADMISSION_INVALID_MODE");
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var admissionId = existing?.Id ?? Guid.NewGuid().ToString("D");
        var chargeId = existing?.ChargeRecordId;
        var receiptId = existing?.ReceiptRecordId;
        var net = request.Amount - request.DiscountAmount;
        if (existing is null)
        {
            chargeId = request.Amount > 0 ? Guid.NewGuid().ToString("D") : null;
            receiptId = request.Mode == "received" && net > 0 ? Guid.NewGuid().ToString("D") : null;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ORDERING_SESSION_ADMISSIONS
                    (ID, SESSION_ID, SOURCE_PERIOD_ID, AMOUNT, DISCOUNT_AMOUNT, CREDIT_AMOUNT, STATUS,
                     CASH_PERIOD_ID, CHARGE_RECORD_ID, RECEIPT_RECORD_ID, VERSION, REASON,
                     CREATED_AT, CREATED_BY, UPDATED_AT, UPDATED_BY)
                VALUES (@Id,@SessionId,@SourcePeriodId,@Amount,@DiscountAmount,@CreditAmount,@Status,
                        @CashPeriodId,@ChargeRecordId,@ReceiptRecordId,0,@Reason,@Now,@ActorId,@Now,@ActorId);
                """, new { Id=admissionId, SessionId=sessionId, SourcePeriodId=session.SourcePeriodId,
                    request.Amount, request.DiscountAmount, request.CreditAmount,
                    Status = request.Mode, request.CashPeriodId, ChargeRecordId=chargeId, ReceiptRecordId=receiptId,
                    request.Reason, Now=now, ActorId=actorId }, transaction, cancellationToken:ct));
            if (request.CreditAmount > 0 && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM ORDERS WHERE SESSION_ID=@SessionId;", new {SessionId=sessionId}, transaction, cancellationToken:ct)) == 0)
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE CUSTOMER_ORDER_SESSIONS SET PREPAID_MEAL_CREDIT=@Credit, REMAINING_MEAL_CREDIT=@Credit,
                        UPDATED_AT=@Now, UPDATED_BY=@ActorId WHERE ID=@SessionId;
                    """, new { Credit=request.CreditAmount, Now=now, ActorId=actorId, SessionId=sessionId }, transaction, cancellationToken:ct));
            if (chargeId is not null) await InsertAdmissionRecordAsync(connection, transaction, sessionId, chargeId,
                "charge_add", request.Amount, "none", request.Reason, null, now, actorId, accountVersion + 1, ct);
            if (request.DiscountAmount > 0) await InsertAdmissionRecordAsync(connection, transaction, sessionId,
                Guid.NewGuid().ToString("D"), "charge_reduce", request.DiscountAmount, "none", "入場優惠／折讓", null,
                now, actorId, accountVersion + (chargeId is null ? 1 : 2), ct);
            if (receiptId is not null) await InsertAdmissionRecordAsync(connection, transaction, sessionId, receiptId,
                "cash_receipt", net, "none", "入場實收", request.CashPeriodId, now, actorId,
                accountVersion + 1 + (request.DiscountAmount > 0 ? 1 : 0), ct);
            if (request.Mode == "unpaid" && chargeId is not null)
                await CreateCaseAsync(connection, transaction, sessionId, chargeId, request.Amount,
                    "unpaid", request.Reason, session.SourcePeriodId, now, actorId, ct);
            else if (receiptId is not null && request.CashPeriodId is null)
                await CreateCaseAsync(connection, transaction, sessionId, receiptId, net,
                    "pending_allocation", "入場實收待歸屬", session.SourcePeriodId, now, actorId, ct);
            var increments = (chargeId is not null ? 1 : 0) + (request.DiscountAmount > 0 ? 1 : 0) + (receiptId is not null ? 1 : 0);
            await connection.ExecuteAsync(new CommandDefinition("UPDATE ORDERING_FINANCE_ACCOUNTS SET VERSION=VERSION+@Count, UPDATED_AT=@Now WHERE SESSION_ID=@SessionId;",
                new { Count=increments, Now=now, SessionId=sessionId }, transaction, cancellationToken:ct));
            accountVersion += increments;
        }
        var result = new OrderingAdmissionOperationDto(request.OperationId, admissionId, chargeId, receiptId, accountVersion);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_OPERATIONS (OPERATION_ID,SESSION_ID,PAYLOAD_HASH,RESULT_JSON,CREATED_AT,CREATED_BY)
            VALUES (@OperationId,@SessionId,@PayloadHash,@ResultJson,@Now,@ActorId);
            """, new { request.OperationId, SessionId=sessionId, PayloadHash=payloadHash,
                ResultJson=JsonSerializer.Serialize(result), Now=now, ActorId=actorId }, transaction, cancellationToken:ct));
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<OrderingFinanceCaseOperationDto> ResolveCaseAsync(string sessionId, string caseId,
        ResolveOrderingFinanceCaseRequest request, string payloadHash, string actorId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var session = await GetSessionAsync(connection, transaction, sessionId, true, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition(
            "SELECT SESSION_ID AS SessionId,PAYLOAD_HASH AS PayloadHash,RESULT_JSON AS ResultJson FROM ORDERING_FINANCE_OPERATIONS WHERE OPERATION_ID=@OperationId;",
            new {request.OperationId}, transaction, cancellationToken:ct));
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.PayloadHash != payloadHash)
                throw new BusinessException("相同操作識別碼已用於不同內容。", "FINANCE_OPERATION_CONFLICT");
            return JsonSerializer.Deserialize<OrderingFinanceCaseOperationDto>(prior.ResultJson)
                ?? throw new InvalidOperationException("Invalid case operation snapshot.");
        }
        var item = await connection.QuerySingleOrDefaultAsync<CaseRow>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, RECORD_ID AS RecordId, ORDER_ID AS OrderId,
                   ORDER_ITEM_ID AS OrderItemId, CASE_KIND AS CaseKind, AMOUNT AS Amount, PROFIT_SCOPE AS ProfitScope,
                   STATUS AS Status, SOURCE_PERIOD_ID AS SourcePeriodId, DESCRIPTION AS Description,
                   CREATED_AT AS CreatedAt, CREATED_BY AS CreatedBy, UPDATED_AT AS UpdatedAt, UPDATED_BY AS UpdatedBy,
                   RESOLVED_AT AS ResolvedAt, RESOLVED_BY AS ResolvedBy, RESOLUTION_NOTE AS ResolutionNote
            FROM ORDERING_FINANCE_CASES WHERE ID=@CaseId AND SESSION_ID=@SessionId FOR UPDATE;
            """, new {CaseId=caseId, SessionId=sessionId}, transaction, cancellationToken:ct));
        if (item is null) throw new BusinessException("找不到未決案件。", "FINANCE_CASE_NOT_FOUND");
        if (item.Status == "resolved") return new OrderingFinanceCaseOperationDto(request.OperationId, caseId, 0);
        var version = await EnsureAccountAsync(connection, transaction, session, ct);
        if (version != request.ExpectedVersion) throw new BusinessException("帳款已更新，請重新讀取。", "FINANCE_VERSION_CONFLICT");
        if (request.CashPeriodId is not null && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM BUSINESS_PERIODS WHERE ID=@Id;", new {Id=request.CashPeriodId}, transaction, cancellationToken:ct)) != 1)
            throw new BusinessException("指定的收退款營業期不存在。", "FINANCE_CASH_PERIOD_NOT_FOUND");
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        if (item.RecordId is not null)
        {
            var record = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(RecordsSql + " AND ID=@RecordId FOR UPDATE;",
                new {SessionId=sessionId, RecordId=item.RecordId}, transaction, cancellationToken:ct));
            if (record is null) throw new BusinessException("未決案件原紀錄不存在。", "FINANCE_RECORD_NOT_FOUND");
            record.Version += 1; record.CashPeriodId = request.CashPeriodId ?? record.CashPeriodId;
            record.AllocationStatus = "confirmed"; record.HoldScope = "none"; record.CaseId = caseId;
            record.UpdatedAt = now; record.UpdatedBy = actorId; record.ConfirmedAt = now; record.ConfirmedBy = actorId;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ORDERING_FINANCE_RECORDS SET VERSION=@Version,CASH_PERIOD_ID=@CashPeriodId,
                    ALLOCATION_STATUS='confirmed',HOLD_SCOPE='none',UPDATED_AT=@UpdatedAt,UPDATED_BY=@UpdatedBy,
                    CONFIRMED_AT=@ConfirmedAt,CONFIRMED_BY=@ConfirmedBy WHERE ID=@Id AND SESSION_ID=@SessionId;
                INSERT INTO ORDERING_FINANCE_REVISIONS (RECORD_ID,VERSION,OPERATION_ID,SNAPSHOT_JSON,RECORDED_AT,RECORDED_BY)
                    VALUES (@Id,@Version,@OperationId,@Snapshot,@UpdatedAt,@UpdatedBy);
                UPDATE ORDERING_FINANCE_ACCOUNTS SET VERSION=VERSION+1,UPDATED_AT=@UpdatedAt WHERE SESSION_ID=@SessionId;
                """, new { record.Id, record.Version, record.CashPeriodId, record.UpdatedAt,
                    record.UpdatedBy, record.ConfirmedAt, record.ConfirmedBy, SessionId=sessionId,
                    request.OperationId, Snapshot=JsonSerializer.Serialize(Map(record)) }, transaction, cancellationToken:ct));
            version++;
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERING_FINANCE_CASES SET STATUS='resolved',RESOLVED_AT=@Now,RESOLVED_BY=@ActorId,
                RESOLUTION_NOTE=@Note,UPDATED_AT=@Now,UPDATED_BY=@ActorId WHERE ID=@CaseId AND SESSION_ID=@SessionId;
            INSERT INTO ORDERING_FINANCE_OPERATIONS (OPERATION_ID,SESSION_ID,PAYLOAD_HASH,RESULT_JSON,CREATED_AT,CREATED_BY)
                VALUES (@OperationId,@SessionId,@PayloadHash,@ResultJson,@Now,@ActorId);
            """, new {Now=now, ActorId=actorId, Note=string.IsNullOrWhiteSpace(request.ResolutionNote)?null:request.ResolutionNote.Trim(),
                CaseId=caseId, SessionId=sessionId, request.OperationId, PayloadHash=payloadHash,
                ResultJson=JsonSerializer.Serialize(new OrderingFinanceCaseOperationDto(request.OperationId, caseId, version))}, transaction, cancellationToken:ct));
        await transaction.CommitAsync(ct);
        return new OrderingFinanceCaseOperationDto(request.OperationId, caseId, version);
    }

    public async Task<OrderingSessionDepartureDto> UpdateDepartureAsync(string sessionId,
        UpdateOrderingSessionDepartureRequest request, string payloadHash, string actorId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var session = await GetSessionAsync(connection, transaction, sessionId, true, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition(
            "SELECT SESSION_ID AS SessionId,PAYLOAD_HASH AS PayloadHash,RESULT_JSON AS ResultJson FROM ORDERING_FINANCE_OPERATIONS WHERE OPERATION_ID=@OperationId;",
            new {request.OperationId}, transaction, cancellationToken:ct));
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.PayloadHash != payloadHash)
                throw new BusinessException("相同操作識別碼已用於不同內容。", "FINANCE_OPERATION_CONFLICT");
            return JsonSerializer.Deserialize<OrderingSessionDepartureDto>(prior.ResultJson)
                ?? throw new InvalidOperationException("Invalid departure operation snapshot.");
        }
        var action = request.Action.Trim().ToLowerInvariant();
        if (action is not ("depart" or "void" or "reopen" or "reissue"))
            throw new BusinessException("離店操作無效。", "SESSION_DEPARTURE_ACTION_INVALID");
        if (action == "void" && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT (SELECT COUNT(*) FROM ORDERS WHERE SESSION_ID=@SessionId) + (SELECT COUNT(*) FROM ORDERING_FINANCE_RECORDS WHERE SESSION_ID=@SessionId AND KIND IN ('cash_receipt','cash_refund'));",
            new {SessionId=sessionId}, transaction, cancellationToken:ct)) > 0)
            throw new BusinessException("已有消費或款項的點餐碼不能作廢，請走正常離店與退款流程。", "SESSION_VOID_HAS_USAGE");
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var entry = action == "reopen" || action == "reissue" ? "open" : action == "void" ? "voided" : "departed";
        var status = entry == "open" ? "active" : "readonly";
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE CUSTOMER_ORDER_SESSIONS SET ENTRY_STATUS=@EntryStatus,SESSION_STATUS=@SessionStatus,
                DEPARTED_AT=CASE WHEN @EntryStatus='departed' THEN @Now ELSE DEPARTED_AT END,
                DEPARTURE_REASON=CASE WHEN @EntryStatus='open' THEN NULL ELSE @Reason END,
                UPDATED_AT=@Now,UPDATED_BY=@ActorId WHERE ID=@SessionId;
            """, new {EntryStatus=entry, SessionStatus=status, Now=now, Reason=request.Reason.Trim(), ActorId=actorId, SessionId=sessionId}, transaction, cancellationToken:ct));
        var result = new OrderingSessionDepartureDto(sessionId, entry, status, entry == "open", entry == "departed" ? Offset(now) : null, request.Reason.Trim());
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_OPERATIONS (OPERATION_ID,SESSION_ID,PAYLOAD_HASH,RESULT_JSON,CREATED_AT,CREATED_BY)
            VALUES (@OperationId,@SessionId,@PayloadHash,@ResultJson,@Now,@ActorId);
            """, new {request.OperationId, SessionId=sessionId, PayloadHash=payloadHash, ResultJson=JsonSerializer.Serialize(result), Now=now, ActorId=actorId}, transaction, cancellationToken:ct));
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<long> EnsureAccountAsync(MySqlConnection connection, MySqlTransaction transaction,
        SessionRow session, CancellationToken ct)
    {
        if (session.SourcePeriodId is null) throw new BusinessException("顧客沒有來源營業期。", "FINANCE_SOURCE_PERIOD_REQUIRED");
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_ACCOUNTS (SESSION_ID,SOURCE_PERIOD_ID,VERSION,UPDATED_AT)
            VALUES (@SessionId,@SourcePeriodId,0,@Now) ON DUPLICATE KEY UPDATE SESSION_ID=VALUES(SESSION_ID);
            """, new {SessionId=session.SessionId, SourcePeriodId=session.SourcePeriodId, Now=now}, transaction, cancellationToken:ct));
        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT VERSION FROM ORDERING_FINANCE_ACCOUNTS WHERE SESSION_ID=@SessionId FOR UPDATE;",
            new {SessionId=session.SessionId}, transaction, cancellationToken:ct));
    }

    private static async Task InsertAdmissionRecordAsync(MySqlConnection connection, MySqlTransaction transaction,
        string sessionId, string id, string kind, long amount, string holdScope, string reason,
        string? cashPeriodId, DateTime now, string actorId, long version, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_RECORDS
                (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,CASH_PERIOD_ID,OCCURRED_AT,
                 ALLOCATION_STATUS,HOLD_SCOPE,REASON,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY,CONFIRMED_AT,CONFIRMED_BY)
            SELECT @Id,@SessionId,@Version,@Kind,@Amount,'admission',BUSINESS_PERIOD_ID,@CashPeriodId,@Now,
                   CASE WHEN @CashPeriodId IS NULL AND @Kind='cash_receipt' THEN 'pending' ELSE 'confirmed' END,
                   CASE WHEN @CashPeriodId IS NULL AND @Kind='cash_receipt' THEN 'session' ELSE @HoldScope END,
                   @Reason,@Now,@ActorId,@Now,@ActorId,
                   CASE WHEN @CashPeriodId IS NULL AND @Kind='cash_receipt' THEN NULL ELSE @Now END,
                   CASE WHEN @CashPeriodId IS NULL AND @Kind='cash_receipt' THEN NULL ELSE @ActorId END
            FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId;
            """, new {Id=id, SessionId=sessionId, Version=version, Kind=kind, Amount=amount, HoldScope=holdScope,
                Reason=reason, CashPeriodId=cashPeriodId, Now=now, ActorId=actorId}, transaction, cancellationToken:ct));
    }

    private static async Task CreateCaseAsync(MySqlConnection connection, MySqlTransaction transaction,
        string sessionId, string recordId, long amount, string kind, string description, string sourcePeriodId,
        DateTime now, string actorId, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("D");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERING_FINANCE_RECORDS SET CASE_ID=@CaseId, ALLOCATION_STATUS='pending', HOLD_SCOPE='session'
            WHERE ID=@RecordId AND SESSION_ID=@SessionId;
            INSERT INTO ORDERING_FINANCE_CASES
                (ID,SESSION_ID,RECORD_ID,CASE_KIND,AMOUNT,PROFIT_SCOPE,STATUS,SOURCE_PERIOD_ID,DESCRIPTION,
                 CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@CaseId,@SessionId,@RecordId,@Kind,@Amount,'session','open',@SourcePeriodId,@Description,
                    @Now,@ActorId,@Now,@ActorId);
            """, new {CaseId=id, RecordId=recordId, SessionId=sessionId, Kind=kind, Amount=amount,
                SourcePeriodId=sourcePeriodId, Description=description, Now=now, ActorId=actorId}, transaction, cancellationToken:ct));
    }

    private static OrderingAdmissionDto MapAdmission(AdmissionRow x)
        => new(x.Id, x.SessionId, x.Amount, x.DiscountAmount, x.CreditAmount, x.Status, x.CashPeriodId,
            x.ChargeRecordId, x.ReceiptRecordId, x.Version, x.Reason, Offset(x.CreatedAt), Offset(x.UpdatedAt));

    private static OrderingFinanceCaseDto MapCase(CaseRow x)
        => new(x.Id, x.SessionId, x.RecordId, x.OrderId, x.OrderItemId, x.CaseKind, x.Amount, x.ProfitScope,
            x.Status, x.SourcePeriodId, x.Description, Offset(x.CreatedAt), x.CreatedBy, Offset(x.UpdatedAt),
            x.UpdatedBy, x.ResolvedAt is { } resolved ? Offset(resolved) : null, x.ResolvedBy, x.ResolutionNote);

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
            CaseId = original?.CaseId ?? (request.AllocationStatus == "pending" ||
                (request.Kind is "cash_receipt" or "cash_refund" && cashPeriodId is null)
                ? Guid.NewGuid().ToString("D") : null),
            CreatedAt = original?.CreatedAt ?? now, CreatedBy = original?.CreatedBy ?? actorId,
            UpdatedAt = now, UpdatedBy = actorId,
            ConfirmedAt = request.AllocationStatus == "confirmed" ? now : null,
            ConfirmedBy = request.AllocationStatus == "confirmed" ? actorId : null
        };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_RECORDS
                (ID, SESSION_ID, VERSION, KIND, AMOUNT, SOURCE_KIND, ORDER_ID, ORDER_ITEM_ID,
                 SOURCE_PERIOD_ID, CASH_PERIOD_ID, OCCURRED_AT, ALLOCATION_STATUS, HOLD_SCOPE,
                 REASON, REVERSES_RECORD_ID, CASE_ID, CREATED_AT, CREATED_BY, UPDATED_AT, UPDATED_BY, CONFIRMED_AT, CONFIRMED_BY)
            VALUES (@Id, @SessionId, @Version, @Kind, @Amount, @SourceKind, @OrderId, @OrderItemId,
                    @SourcePeriodId, @CashPeriodId, @OccurredAt, @AllocationStatus, @HoldScope,
                    @Reason, @ReversesRecordId, @CaseId, @CreatedAt, @CreatedBy, @UpdatedAt, @UpdatedBy, @ConfirmedAt, @ConfirmedBy)
            ON DUPLICATE KEY UPDATE VERSION = VALUES(VERSION), AMOUNT = VALUES(AMOUNT),
                SOURCE_KIND = VALUES(SOURCE_KIND), ORDER_ID = VALUES(ORDER_ID), ORDER_ITEM_ID = VALUES(ORDER_ITEM_ID),
                CASH_PERIOD_ID = VALUES(CASH_PERIOD_ID), OCCURRED_AT = VALUES(OCCURRED_AT),
                ALLOCATION_STATUS = VALUES(ALLOCATION_STATUS), HOLD_SCOPE = VALUES(HOLD_SCOPE),
                REASON = VALUES(REASON), CASE_ID = VALUES(CASE_ID), UPDATED_AT = VALUES(UPDATED_AT), UPDATED_BY = VALUES(UPDATED_BY),
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
        if (row.CaseId is not null)
        {
            var caseKind = request.Kind == "cash_refund" ? "pending_refund" :
                request.Kind == "cash_receipt" ? "pending_allocation" : "pending_allocation";
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ORDERING_FINANCE_CASES
                    (ID, SESSION_ID, RECORD_ID, ORDER_ID, ORDER_ITEM_ID, CASE_KIND, AMOUNT, PROFIT_SCOPE,
                     STATUS, SOURCE_PERIOD_ID, DESCRIPTION, CREATED_AT, CREATED_BY, UPDATED_AT, UPDATED_BY)
                VALUES (@Id, @SessionId, @RecordId, @OrderId, @OrderItemId, @CaseKind, @Amount, @ProfitScope,
                        @Status, @SourcePeriodId, @Description, @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE STATUS = @Status, AMOUNT = @Amount, UPDATED_AT = @Now,
                    UPDATED_BY = @ActorId, DESCRIPTION = @Description;
                """, new { Id = row.CaseId, SessionId = sessionId, RecordId = row.Id, row.OrderId,
                    row.OrderItemId, CaseKind = caseKind, row.Amount,
                    ProfitScope = row.HoldScope == "none" ? "session" : row.HoldScope,
                    Status = row.AllocationStatus == "confirmed" && (row.Kind is not ("cash_receipt" or "cash_refund") || row.CashPeriodId is not null) ? "resolved" : "open",
                    SourcePeriodId = row.SourcePeriodId, Description = row.Reason, Now = now, ActorId = actorId },
                transaction, cancellationToken: ct));
        }
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
        x.AllocationStatus, x.HoldScope, x.Reason, x.ReversesRecordId, x.CaseId, Offset(x.CreatedAt), x.CreatedBy,
        Offset(x.UpdatedAt), x.UpdatedBy, x.ConfirmedAt is { } confirmed ? Offset(confirmed) : null, x.ConfirmedBy);

    private sealed class SessionRow
    {
        public string SessionId { get; set; } = "";
        public DateTime BusinessDate { get; set; }
        public string? SourcePeriodId { get; set; }
        public int FlowVersion { get; set; }
        public string EntryStatus { get; set; } = "open";
        public DateTime? DepartedAt { get; set; }
        public string? DepartureReason { get; set; }
        public string SessionStatus { get; set; } = "active";
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
    private sealed class AdmissionRow
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public long Amount { get; set; }
        public long DiscountAmount { get; set; }
        public long CreditAmount { get; set; }
        public string Status { get; set; } = "";
        public string? CashPeriodId { get; set; }
        public string? ChargeRecordId { get; set; }
        public string? ReceiptRecordId { get; set; }
        public long Version { get; set; }
        public string Reason { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    private sealed class CaseRow
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string? RecordId { get; set; }
        public string? OrderId { get; set; }
        public string? OrderItemId { get; set; }
        public string CaseKind { get; set; } = "";
        public long Amount { get; set; }
        public string ProfitScope { get; set; } = "session";
        public string Status { get; set; } = "open";
        public string SourcePeriodId { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public string? ResolutionNote { get; set; }
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
        public string? CaseId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime? ConfirmedAt { get; set; }
        public string? ConfirmedBy { get; set; }
    }
}
