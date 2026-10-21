using System.Data;
using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed partial class SettlementRepository
{
    public async Task<SettlementRecalculationPreviewDto> RecalculateFinalizedAsync(
        DateOnly businessDate, int sessionNo, ConfirmSettlementRecalculationRequest? confirmation,
        Func<SettlementRunRow, SettlementSourceData, IReadOnlyList<SettlementPaymentEventRow>,
            IReadOnlyList<SettlementCorrectionEventRow>, SettlementRecalculationPlan> prepare,
        string actorId, DateTime now, CancellationToken cancellationToken)
    {
        if (sessionNo < 1) throw new BusinessException("營業時段必須大於零。", "SETTLEMENT_SESSION_INVALID");
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        // Confirmation locks both the settlement and all rows/ranges used by the calculation.
        // A preview is a consistent read and performs no writes.
        await using var transaction = await connection.BeginTransactionAsync(
            confirmation is null ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
        if (confirmation is not null)
            await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT ID FROM ORDERING_RUNTIME_LOCKS WHERE ID='operating_period' FOR UPDATE;",
                transaction: transaction, cancellationToken: cancellationToken));
        var run = await connection.QuerySingleOrDefaultAsync<SettlementRunRow>(new CommandDefinition(
            RunSelectSql + " WHERE BUSINESS_DATE=@Date AND SESSION_NO=@SessionNo"
                + (confirmation is null ? ";" : " FOR UPDATE;"),
            new { Date = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo }, transaction,
            cancellationToken: cancellationToken));
        if (run?.Status != "finalized")
            throw new ConflictException("只有已正式結算的時段可進行補正後重新結算。", "SETTLEMENT_RECALCULATION_REQUIRES_FINALIZED");
        var otherFinalized = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM SETTLEMENT_RUNS WHERE BUSINESS_DATE=@Date AND ID<>@Id AND STATUS='finalized';",
            new { Date = run.BusinessDate, Id = run.Id }, transaction, cancellationToken: cancellationToken));
        if (otherFinalized > 0)
            throw new ConflictException("同日有其他正式結算時段，請先核對分段結算範圍。", "SETTLEMENT_RECALCULATION_AMBIGUOUS");

        if (confirmation is not null)
        {
            var prior = await connection.QuerySingleOrDefaultAsync<RecalculationAuditRow>(new CommandDefinition(
                "SELECT SETTLEMENT_ID AS SettlementId, ACTION_TYPE AS ActionType, AFTER_JSON AS AfterJson FROM SETTLEMENT_AUDIT_LOG WHERE ID=@Id;",
                new { Id = confirmation.OperationId }, transaction, cancellationToken: cancellationToken));
            if (prior is not null)
            {
                var payload = prior.ActionType == "settlement_recalculated"
                    ? JsonSerializer.Deserialize<RecalculationAuditPayload>(prior.AfterJson) : null;
                if (prior.SettlementId != run.Id || payload is null || payload.ActorId != actorId
                    || payload.SourceToken != confirmation.SourceToken
                    || payload.Reason != confirmation.Reason.Trim())
                    throw new ConflictException("此操作編號已用於其他操作，請重新預覽。", "SETTLEMENT_OPERATION_CONFLICT");
                await transaction.CommitAsync(cancellationToken);
                return payload.Preview;
            }
        }

        var source = await GetSourceDataCoreAsync(connection, transaction, run.Id, businessDate, cancellationToken);
        var payments = (await connection.QueryAsync<SettlementPaymentEventRow>(new CommandDefinition("""
            SELECT ID AS Id, SETTLEMENT_ID AS SettlementId, STAFF_ID AS StaffId, EVENT_KIND AS EventKind,
                   AMOUNT AS Amount, OPERATION_ID AS OperationId, REASON AS Reason,
                   CREATED_AT AS CreatedAt, CREATED_BY AS CreatedBy
            FROM SETTLEMENT_PAYMENT_EVENTS WHERE SETTLEMENT_ID=@Id;
            """, new { Id = run.Id }, transaction, cancellationToken: cancellationToken))).AsList();
        var corrections = (await connection.QueryAsync<SettlementCorrectionEventRow>(new CommandDefinition("""
            SELECT ID AS Id, SETTLEMENT_ID AS SettlementId, CORRECTION_OF_ID AS CorrectionOfId,
                   SOURCE_KIND AS SourceKind, SOURCE_ID AS SourceId, STAFF_ID AS StaffId,
                   AMOUNT_DELTA AS AmountDelta, STATUS AS Status, OPERATION_ID AS OperationId,
                   REASON AS Reason, CREATED_AT AS CreatedAt, CREATED_BY AS CreatedBy
            FROM SETTLEMENT_CORRECTION_EVENTS WHERE SETTLEMENT_ID=@Id;
            """, new { Id = run.Id }, transaction, cancellationToken: cancellationToken))).AsList();
        var plan = prepare(run, source, payments, corrections);
        if (confirmation is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return plan.Preview;
        }
        plan.ValidateConfirmation(confirmation);
        await MarkOrderBackfillsAppliedAsync(connection, transaction, run.Id, run.BusinessDate, cancellationToken);

        var beforeJson = JsonSerializer.Serialize(new { Run = run, source.StaffInputs, source.Results, payments, corrections });
        foreach (var result in plan.Calculation.Results)
        {
            result.Id = result.StaffId is null ? Guid.NewGuid().ToString("D")
                : source.Results.SingleOrDefault(x => x.StaffId == result.StaffId && x.Role == result.Role)?.Id
                    ?? Guid.NewGuid().ToString("D");
            result.SettlementId = run.Id;
        }
        var retainedInputIds = plan.Calculation.StaffInputs.Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (retainedInputIds.Length == 0)
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM SETTLEMENT_STAFF_INPUTS WHERE SETTLEMENT_ID=@SettlementId;",
                new { SettlementId = run.Id }, transaction, cancellationToken: cancellationToken));
        else
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM SETTLEMENT_STAFF_INPUTS WHERE SETTLEMENT_ID=@SettlementId AND ID NOT IN @RetainedInputIds;",
                new { SettlementId = run.Id, RetainedInputIds = retainedInputIds }, transaction,
                cancellationToken: cancellationToken));
        await SaveCalculationCoreAsync(connection, transaction, run.Id, plan.Calculation, actorId, now,
            true, cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE SETTLEMENT_RUNS
            SET STATUS='finalized', CORRECTION_VERSION=CORRECTION_VERSION+1,
                UPDATED_AT=@Now, UPDATED_BY=@ActorId
            WHERE ID=@Id AND STATUS='calculated' AND INPUT_VERSION=@NextInputVersion
                AND CORRECTION_VERSION=@ExpectedCorrectionVersion;
            """, new { Id = run.Id, Now = now, ActorId = actorId, NextInputVersion = run.InputVersion + 1,
                ExpectedCorrectionVersion = run.CorrectionVersion }, transaction, cancellationToken: cancellationToken));
        if (updated != 1)
            throw new ConflictException("結算版本與預期不符，請重新預覽。", "SETTLEMENT_RECALCULATION_CHANGED");
        plan.Calculation.Run.Status = "finalized";
        plan.Calculation.Run.InputVersion = run.InputVersion + 1;
        plan.Calculation.Run.CorrectionVersion = run.CorrectionVersion + 1;
        var after = new RecalculationAuditPayload(confirmation.OperationId, confirmation.SourceToken,
            run.CorrectionVersion + 1, confirmation.Reason.Trim(), actorId, plan.Preview,
            plan.Calculation.Run, plan.Calculation.StaffInputs, plan.Calculation.Results)
        { PublicTipReconciliationReason = confirmation.Adjustments?.PublicTipReconciliationReason?.Trim() };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO SETTLEMENT_AUDIT_LOG
                (ID, SETTLEMENT_ID, ACTION_TYPE, BEFORE_JSON, AFTER_JSON, REASON, ACTOR_ID, CREATED_AT)
            VALUES (@Id,@SettlementId,'settlement_recalculated',@BeforeJson,@AfterJson,@Reason,@ActorId,@Now);
            """, new { Id = confirmation.OperationId, SettlementId = run.Id, BeforeJson = beforeJson,
                AfterJson = JsonSerializer.Serialize(after), Reason = confirmation.Reason.Trim(),
                ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return plan.Preview;
    }

    public async Task<IReadOnlyList<SettlementRecalculationHistoryDto>> GetRecalculationHistoryAsync(
        DateOnly businessDate, int sessionNo, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<RecalculationHistoryRow>("""
            SELECT A.ID AS Id, CAST(JSON_UNQUOTE(JSON_EXTRACT(A.AFTER_JSON,'$.Version')) AS SIGNED) AS Version,
                   CAST(JSON_UNQUOTE(JSON_EXTRACT(A.AFTER_JSON,'$.Preview.PreviousPayroll')) AS DECIMAL(20,2)) AS PreviousPayroll,
                   CAST(JSON_UNQUOTE(JSON_EXTRACT(A.AFTER_JSON,'$.Preview.CorrectedPayroll')) AS DECIMAL(20,2)) AS CorrectedPayroll,
                   CAST(JSON_UNQUOTE(JSON_EXTRACT(A.AFTER_JSON,'$.Preview.PayrollDelta')) AS DECIMAL(20,2)) AS PayrollDelta,
                   A.REASON AS Reason, A.ACTOR_ID AS ActorId, A.CREATED_AT AS CreatedAt
            FROM SETTLEMENT_AUDIT_LOG A INNER JOIN SETTLEMENT_RUNS R ON R.ID=A.SETTLEMENT_ID
            WHERE R.BUSINESS_DATE=@Date AND R.SESSION_NO=@SessionNo AND A.ACTION_TYPE='settlement_recalculated'
            ORDER BY A.CREATED_AT DESC, Version DESC;
            """, new { Date = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo }, cancellationToken);
        return rows.Select(x => new SettlementRecalculationHistoryDto(x.Id, x.Version, x.PreviousPayroll,
            x.CorrectedPayroll, x.PayrollDelta, x.Reason, x.ActorId,
            new DateTimeOffset(DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Unspecified), TimeSpan.FromHours(8)))).ToArray();
    }

    private sealed class RecalculationAuditRow
    {
        public string SettlementId { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string AfterJson { get; set; } = "";
    }

    private sealed class RecalculationHistoryRow
    {
        public string Id { get; set; } = "";
        public int Version { get; set; }
        public decimal PreviousPayroll { get; set; }
        public decimal CorrectedPayroll { get; set; }
        public decimal PayrollDelta { get; set; }
        public string Reason { get; set; } = "";
        public string ActorId { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private sealed record RecalculationAuditPayload(string OperationId, string SourceToken, int Version,
        string Reason, string ActorId, SettlementRecalculationPreviewDto Preview, SettlementRunRow Run,
        IReadOnlyList<SettlementStaffInputRow> StaffInputs, IReadOnlyList<SettlementResultLineRow> Results)
    {
        public string? PublicTipReconciliationReason { get; init; }
    }
}
