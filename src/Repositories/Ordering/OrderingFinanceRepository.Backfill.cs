using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingFinanceRepository
{
    // Called while the order transaction owns the customer-session lock. No receipt is inserted.
    internal static async Task LinkBackfillCashAsync(MySqlConnection connection, MySqlTransaction tx,
        NewOrderAggregate order, CancellationToken ct)
    {
        var backfill = order.Backfill!;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_FINANCE_ACCOUNTS (SESSION_ID,SOURCE_PERIOD_ID,VERSION,UPDATED_AT)
            SELECT ID,BUSINESS_PERIOD_ID,0,@Now FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId
            ON DUPLICATE KEY UPDATE SESSION_ID=VALUES(SESSION_ID);
            """, new { order.SessionId, Now = order.SubmittedAt }, tx, cancellationToken: ct));
        var version = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT VERSION FROM ORDERING_FINANCE_ACCOUNTS WHERE SESSION_ID=@SessionId FOR UPDATE;",
            new { order.SessionId }, tx, cancellationToken: ct));
        if (version != backfill.ExpectedFinanceVersion)
            throw new ConflictException("帳款已更新，請重新讀取後核對補登。", "FINANCE_VERSION_CONFLICT");
        var duplicateCharge = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM ORDERING_FINANCE_RECORDS
                WHERE SESSION_ID=@SessionId AND KIND='charge_add' AND SOURCE_KIND='unallocated'
                  AND VOIDED_AT IS NULL AND AMOUNT=@Amount AND AMOUNT>0);
            """, new { order.SessionId, Amount = order.TotalAmount }, tx, cancellationToken: ct));
        if (duplicateCharge)
            throw new ConflictException("已有同額未歸屬加收應付，請先核對並更正原紀錄，避免本次補登重複增加應收。", "ORDER_BACKFILL_CHARGE_DUPLICATE");
        if (backfill.LinkedCashRecordId is not null)
        {
            var record = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(
                RecordsSql + " AND ID=@Id FOR UPDATE;", new { order.SessionId, Id = backfill.LinkedCashRecordId },
                tx, cancellationToken: ct));
            if (record is null || record.VoidedAt is not null || record.Kind != "cash_receipt"
                || record.SourceKind != "unallocated" || record.OrderId is not null || record.CashPeriodId is null
                || record.AllocationStatus != "confirmed" || record.HoldScope != "none"
                || record.Version != backfill.ExpectedCashRecordVersion || record.Amount > order.TotalAmount)
                throw new ConflictException("收款已變更或不能整筆歸屬本訂單，請先至帳款確認或拆分收款。", "ORDER_BACKFILL_CASH_CHANGED");
            if (order.Tips.Count > 0 && await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM ORDERING_FINANCE_RECORDS WHERE SESSION_ID=@SessionId
                    AND KIND='tip_supplement' AND LINKED_CASH_RECORD_ID=@Id AND VOIDED_AT IS NULL);
                """, new { order.SessionId, record.Id }, tx, cancellationToken: ct)))
                throw new ConflictException("此收款已有補登小費分配，請先核對既有小費，避免重複計薪。", "ORDER_BACKFILL_TIP_DUPLICATE");
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM ORDERING_FINANCE_RECORDS WHERE SESSION_ID=@SessionId
                    AND KIND='cash_refund' AND REVERSES_RECORD_ID=@Id AND VOIDED_AT IS NULL);
                """, new { order.SessionId, record.Id }, tx, cancellationToken: ct)))
                throw new ConflictException("此收款已有退款，請先在帳款核對收退款歸屬。", "ORDER_BACKFILL_CASH_REFUNDED");
            record.Version++;
            record.SourceKind = "order";
            record.OrderId = order.Id;
            record.UpdatedAt = order.SubmittedAt;
            record.UpdatedBy = order.ActorId!;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ORDERING_FINANCE_RECORDS SET SOURCE_KIND='order',ORDER_ID=@OrderId,
                    VERSION=@Version,UPDATED_AT=@UpdatedAt,UPDATED_BY=@UpdatedBy WHERE ID=@Id;
                INSERT INTO ORDERING_FINANCE_REVISIONS
                    (RECORD_ID,VERSION,OPERATION_ID,SNAPSHOT_JSON,RECORDED_AT,RECORDED_BY)
                VALUES (@Id,@Version,@OperationId,@Snapshot,@UpdatedAt,@UpdatedBy);
                """, new { record.Id, record.Version, record.OrderId, record.UpdatedAt, record.UpdatedBy,
                backfill.OperationId, Snapshot = JsonSerializer.Serialize(Map(record)) }, tx, cancellationToken: ct));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERING_FINANCE_ACCOUNTS SET VERSION=VERSION+1,UPDATED_AT=@Now WHERE SESSION_ID=@SessionId;
            """, new { order.SessionId, Now = order.SubmittedAt }, tx, cancellationToken: ct));
    }
}
