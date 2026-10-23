using System.Data;
using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed partial class SettlementRepository
{
    public async Task<SettlementSalaryDispatchDto> GetSalaryDispatchAsync(DateOnly businessDate, int sessionNo,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        // Keep the finalized version, salary rows and markers in one consistent snapshot.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var run = await connection.QuerySingleOrDefaultAsync<SettlementRunRow>(new CommandDefinition(
            RunSelectSql + " WHERE BUSINESS_DATE=@Date AND SESSION_NO=@SessionNo;",
            new { Date = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo }, transaction,
            cancellationToken: cancellationToken));
        var items = new List<SettlementSalaryDispatchItemDto>();
        if (run?.Status == "finalized")
        {
            var rows = await connection.QueryAsync<SalaryDispatchReadRow>(new CommandDefinition("""
                SELECT P.STAFF_ID AS StaffId, COALESCE(M.DISPLAY_NAME,P.STAFF_ID) AS DisplayName,
                       COALESCE(R.Salary,0) AS Salary, COALESCE(D.IS_PAID,0) AS IsPaid,
                       COALESCE(D.VERSION,0) AS Version, D.UPDATED_AT AS UpdatedAt,
                       D.UPDATED_BY AS UpdatedBy, COALESCE(S.DISPLAY_NAME,A.LOGIN_NAME) AS UpdatedByName
                FROM (
                    SELECT STAFF_ID FROM SETTLEMENT_STAFF_INPUTS WHERE SETTLEMENT_ID=@Id
                    UNION SELECT STAFF_ID FROM SETTLEMENT_RESULT_LINES WHERE SETTLEMENT_ID=@Id AND STAFF_ID IS NOT NULL
                    UNION SELECT STAFF_ID FROM SETTLEMENT_SALARY_DISPATCH WHERE SETTLEMENT_ID=@Id
                ) P
                LEFT JOIN STAFF_MEMBERS M ON M.ID=P.STAFF_ID
                LEFT JOIN (
                    SELECT STAFF_ID, SUM(AFTER_ROUNDING) AS Salary FROM SETTLEMENT_RESULT_LINES
                    WHERE SETTLEMENT_ID=@Id AND STAFF_ID IS NOT NULL GROUP BY STAFF_ID
                ) R ON R.STAFF_ID=P.STAFF_ID
                LEFT JOIN SETTLEMENT_SALARY_DISPATCH D ON D.SETTLEMENT_ID=@Id AND D.STAFF_ID=P.STAFF_ID
                LEFT JOIN ADMIN_USERS A ON A.ID=D.UPDATED_BY
                LEFT JOIN STAFF_MEMBERS S ON S.ID=A.STAFF_MEMBER_ID
                ORDER BY COALESCE(D.IS_PAID,0), COALESCE(M.DISPLAY_NAME,P.STAFF_ID), P.STAFF_ID;
                """, new { run.Id }, transaction, cancellationToken: cancellationToken));
            items.AddRange(rows.Select(row => new SettlementSalaryDispatchItemDto(
                row.StaffId, row.DisplayName, row.Salary, row.IsPaid, row.Version,
                row.UpdatedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null,
                row.UpdatedBy, row.UpdatedByName)));
        }
        await transaction.CommitAsync(cancellationToken);
        return new SettlementSalaryDispatchDto(businessDate, sessionNo, run?.Id, run?.Status == "finalized",
            run?.CorrectionVersion ?? 0, run?.InputVersion ?? 0, items);
    }

    public async Task SaveSalaryDispatchAsync(SaveSettlementSalaryDispatchRequest request, string actorId,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Same settlement lock as recalculation: a marker cannot be saved against a replaced salary version.
        var run = await connection.QuerySingleOrDefaultAsync<SettlementRunRow>(new CommandDefinition(
            RunSelectSql + " WHERE BUSINESS_DATE=@Date AND SESSION_NO=@SessionNo FOR UPDATE;",
            new { Date = request.BusinessDate.ToDateTime(TimeOnly.MinValue), request.SessionNo }, transaction,
            cancellationToken: cancellationToken));
        if (run is null || run.Id != request.SettlementId)
            throw new BusinessException("找不到對應結算，請重新載入。", "SETTLEMENT_DISPATCH_RUN_NOT_FOUND");

        var prior = await connection.QuerySingleOrDefaultAsync<SalaryDispatchAuditRow>(new CommandDefinition("""
            SELECT SETTLEMENT_ID AS SettlementId, ACTION_TYPE AS ActionType, ACTOR_ID AS ActorId, AFTER_JSON AS AfterJson
            FROM SETTLEMENT_AUDIT_LOG WHERE ID=@OperationId;
            """, new { request.OperationId }, transaction, cancellationToken: cancellationToken));
        if (prior is not null)
        {
            var saved = prior.ActionType == "salary_dispatch_changed"
                ? JsonSerializer.Deserialize<SaveSettlementSalaryDispatchRequest>(prior.AfterJson) : null;
            if (prior.SettlementId != run.Id || prior.ActorId != actorId || saved is null
                || JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(request))
                throw new ConflictException("此操作編號已用於其他操作。", "SETTLEMENT_OPERATION_CONFLICT");
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (run.Status != "finalized")
            throw new BusinessException("請先完成正式結算，才能登記薪資派發。", "SETTLEMENT_DISPATCH_REQUIRES_FINALIZED");
        if (run.CorrectionVersion != request.ExpectedCorrectionVersion || run.InputVersion != request.ExpectedInputVersion)
            throw new ConflictException("結算版本已變更，請重新載入後確認。", "SETTLEMENT_DISPATCH_VERSION_CONFLICT");

        var eligible = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM SETTLEMENT_STAFF_INPUTS WHERE SETTLEMENT_ID=@Id AND STAFF_ID=@StaffId)
                OR EXISTS(SELECT 1 FROM SETTLEMENT_RESULT_LINES WHERE SETTLEMENT_ID=@Id AND STAFF_ID=@StaffId)
                OR EXISTS(SELECT 1 FROM SETTLEMENT_SALARY_DISPATCH WHERE SETTLEMENT_ID=@Id AND STAFF_ID=@StaffId);
            """, new { run.Id, request.StaffId }, transaction, cancellationToken: cancellationToken));
        if (eligible == 0)
            throw new BusinessException("此人員不在本次結算範圍。", "SETTLEMENT_DISPATCH_STAFF_INVALID");
        var before = await connection.QuerySingleOrDefaultAsync<SalaryDispatchReadRow>(new CommandDefinition("""
            SELECT IS_PAID AS IsPaid, VERSION AS Version, UPDATED_AT AS UpdatedAt, UPDATED_BY AS UpdatedBy
            FROM SETTLEMENT_SALARY_DISPATCH WHERE SETTLEMENT_ID=@Id AND STAFF_ID=@StaffId FOR UPDATE;
            """, new { run.Id, request.StaffId }, transaction, cancellationToken: cancellationToken));
        if ((before?.Version ?? 0) != request.ExpectedVersion)
            throw new ConflictException("其他管理者已更新派發狀態，請重新載入。", "SETTLEMENT_DISPATCH_VERSION_CONFLICT");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO SETTLEMENT_SALARY_DISPATCH (SETTLEMENT_ID,STAFF_ID,IS_PAID,VERSION,UPDATED_AT,UPDATED_BY)
            VALUES (@Id,@StaffId,@IsPaid,1,@Now,@ActorId)
            ON DUPLICATE KEY UPDATE IS_PAID=@IsPaid, VERSION=VERSION+1, UPDATED_AT=@Now, UPDATED_BY=@ActorId;
            INSERT INTO SETTLEMENT_AUDIT_LOG
                (ID,SETTLEMENT_ID,ACTION_TYPE,BEFORE_JSON,AFTER_JSON,REASON,ACTOR_ID,CREATED_AT)
            VALUES (@OperationId,@Id,'salary_dispatch_changed',@BeforeJson,@AfterJson,@Reason,@ActorId,@Now);
            """, new { run.Id, request.StaffId, request.IsPaid, request.OperationId, Now = now, ActorId = actorId,
                BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(request),
                Reason = request.IsPaid == true ? "薪資派發標記：已給（非支付入帳）" : "薪資派發標記：未給（非追回）" },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class SalaryDispatchReadRow
    {
        public string StaffId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public long Salary { get; set; }
        public bool IsPaid { get; set; }
        public int Version { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedByName { get; set; }
    }

    private sealed class SalaryDispatchAuditRow
    {
        public string SettlementId { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string ActorId { get; set; } = "";
        public string AfterJson { get; set; } = "";
    }
}
