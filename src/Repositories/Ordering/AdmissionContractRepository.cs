using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed record AdmissionPolicySnapshot(long? AdmissionAmount, long Version, int MealCreditAmount);

public interface IAdmissionContractRepository
{
    Task<AdmissionPolicySnapshot> GetPolicyAsync(CancellationToken ct);
    Task<AdmissionPolicySnapshot> SavePolicyAsync(SaveAdmissionPolicyRequest request, string actorId, DateTime now, CancellationToken ct);
    Task<AdmissionCreationOperationRow?> GetOperationAsync(Guid operationId, CancellationToken ct);
}

public sealed class AdmissionCreationOperationRow
{
    public string OperationId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string AdmissionId { get; set; } = "";
    public string BusinessPeriodId { get; set; } = "";
    public DateTime BusinessDate { get; set; }
    public long AdmissionAmount { get; set; }
    public long DiscountAmount { get; set; }
    public int MealCreditAmount { get; set; }
    public string AdmissionStatus { get; set; } = "";
    public long ReceivedAmount { get; set; }
    public string? ChargeRecordId { get; set; }
    public string? ReceiptRecordId { get; set; }
    public string? CashPeriodId { get; set; }
    public long AccountVersion { get; set; }
    public DateTime RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
}

public sealed class AdmissionContractRepository(AppDbContext dbContext) : IAdmissionContractRepository
{
    private const string PolicySql = """
        SELECT P.ADMISSION_AMOUNT AS AdmissionAmount, P.VERSION AS Version,
               S.MINIMUM_MEAL_CREDIT AS MealCreditAmount
        FROM ORDERING_ADMISSION_POLICY P
        INNER JOIN ORDERING_SETTINGS S ON S.ID = 'default'
        WHERE P.ID = 'default'
        """;

    public async Task<AdmissionPolicySnapshot> GetPolicyAsync(CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AdmissionPolicySnapshot>(new CommandDefinition(PolicySql, cancellationToken: ct))
            ?? throw new BusinessException("入場收費設定尚未準備完成。", "ADMISSION_POLICY_NOT_CONFIGURED");
    }

    public async Task<AdmissionPolicySnapshot> SavePolicyAsync(SaveAdmissionPolicyRequest request, string actorId, DateTime now, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var before = await connection.QuerySingleOrDefaultAsync<AdmissionPolicySnapshot>(new CommandDefinition(
            PolicySql + " FOR UPDATE;", transaction: transaction, cancellationToken: ct))
            ?? throw new BusinessException("入場收費設定尚未準備完成。", "ADMISSION_POLICY_NOT_CONFIGURED");
        if (before.Version != request.ExpectedVersion)
            throw new ConflictException("入場收費已更新，請重新讀取後確認。", "ADMISSION_POLICY_VERSION_CONFLICT");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERING_ADMISSION_POLICY
            SET ADMISSION_AMOUNT=@AdmissionAmount, VERSION=VERSION+1, UPDATED_AT=@Now, UPDATED_BY=@ActorId
            WHERE ID='default' AND VERSION=@ExpectedVersion;
            INSERT INTO ORDERING_ADMISSION_POLICY_AUDIT
                (ID, POLICY_VERSION, BEFORE_AMOUNT, AFTER_AMOUNT, REASON, CREATED_AT, CREATED_BY)
            VALUES (@AuditId,@NextVersion,@BeforeAmount,@AdmissionAmount,@Reason,@Now,@ActorId);
            """, new { request.AdmissionAmount, request.ExpectedVersion, Reason = request.Reason.Trim(),
                NextVersion = checked(before.Version + 1), BeforeAmount = before.AdmissionAmount,
                AuditId = Guid.NewGuid().ToString("D"), Now = now, ActorId = actorId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return before with { AdmissionAmount = request.AdmissionAmount, Version = before.Version + 1 };
    }

    public async Task<AdmissionCreationOperationRow?> GetOperationAsync(Guid operationId, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AdmissionCreationOperationRow>(new CommandDefinition("""
            SELECT OPERATION_ID AS OperationId, SESSION_ID AS SessionId, ADMISSION_ID AS AdmissionId,
                   BUSINESS_PERIOD_ID AS BusinessPeriodId, BUSINESS_DATE AS BusinessDate,
                   ADMISSION_AMOUNT AS AdmissionAmount, DISCOUNT_AMOUNT AS DiscountAmount,
                   MEAL_CREDIT_AMOUNT AS MealCreditAmount, ADMISSION_STATUS AS AdmissionStatus,
                   RECEIVED_AMOUNT AS ReceivedAmount, CHARGE_RECORD_ID AS ChargeRecordId,
                   RECEIPT_RECORD_ID AS ReceiptRecordId, CASH_PERIOD_ID AS CashPeriodId,
                   ACCOUNT_VERSION AS AccountVersion, CREATED_AT AS RecordedAt, CREATED_BY AS RecordedBy
            FROM ORDERING_SESSION_CREATE_OPERATIONS WHERE OPERATION_ID=@OperationId;
            """, new { OperationId = operationId.ToString("D") }, cancellationToken: ct));
    }
}
