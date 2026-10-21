using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    public async Task<string?> FindOrderBackfillAsync(string sessionId, string operationId, string payloadHash, CancellationToken ct)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        return await FindOrderBackfillCoreAsync(connection, null, sessionId, operationId, payloadHash, ct);
    }

    private static async Task<string?> FindOrderBackfillCoreAsync(MySqlConnection connection, MySqlTransaction? tx,
        string sessionId, string operationId, string payloadHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BackfillOperation>(new CommandDefinition("""
            SELECT ORDER_ID AS OrderId,SESSION_ID AS SessionId,PAYLOAD_HASH AS PayloadHash
            FROM ORDER_BACKFILLS WHERE OPERATION_ID=@OperationId;
            """, new { OperationId = operationId }, tx, cancellationToken: ct));
        if (row is not null && (row.SessionId != sessionId || row.PayloadHash != payloadHash))
            throw new ConflictException("此補登編號已用於其他內容，請先核對原訂單。", "ORDER_BACKFILL_OPERATION_CONFLICT");
        return row?.OrderId;
    }

    private static async Task CompleteBackfillAsync(MySqlConnection connection, MySqlTransaction tx,
        NewOrderAggregate order, CancellationToken ct)
    {
        var backfill = order.Backfill!;
        var occurredAt = backfill.OccurredAt.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var end = order.Nominees.Select(x => x.ServiceEndsAt).Concat(order.Rooms.Select(x => x.EndsAt))
            .Append(occurredAt).Max();
        var snapshot = new OrderBackfillDto(backfill.OccurredAt.ToOffset(TimeSpan.FromHours(8)),
            new DateTimeOffset(order.SubmittedAt, TimeSpan.FromHours(8)), backfill.Reason.Trim(),
            order.ActorId!, backfill.LinkedCashRecordId);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDER_BACKFILLS
                (ORDER_ID,SESSION_ID,OPERATION_ID,PAYLOAD_HASH,DUPLICATE_HASH,OCCURRED_AT,REASON,
                 LINKED_CASH_RECORD_ID,SNAPSHOT_JSON,CREATED_AT,CREATED_BY)
            VALUES (@Id,@SessionId,@OperationId,@PayloadHash,@DuplicateHash,@OccurredAt,@Reason,
                    @LinkedCashRecordId,@Snapshot,@Now,@ActorId);
            UPDATE ORDERS SET ORDER_STATUS='completed',STORE_CONFIRMATION_STATUS='not_required',
                QUEUE_ENTERED_AT=NULL,CONFIRMED_AT=@OccurredAt,STARTED_AT=@OccurredAt,COMPLETED_AT=@End
            WHERE ID=@Id;
            UPDATE ORDER_NOMINEES SET CONFIRMATION_STATUS='completed',CONFIRMED_AT=@OccurredAt,
                CONFIRMED_BY=@ActorId WHERE ORDER_ID=@Id;
            UPDATE ORDER_FULFILLMENT_UNITS SET ACCEPTED_QUANTITY=QUANTITY,STARTED_QUANTITY=QUANTITY,
                COMPLETED_QUANTITY=QUANTITY,UNIT_STATUS='completed',REST_MINUTES_RESERVED=0,
                ACTUAL_STARTS_AT=COALESCE(SCHEDULED_STARTS_AT,@OccurredAt),
                ACTUAL_ENDS_AT=COALESCE(SCHEDULED_ENDS_AT,@OccurredAt),VERSION=VERSION+1,UPDATED_AT=@Now
            WHERE ORDER_ID=@Id;
            INSERT INTO STAFF_BUSY_BLOCKS
                (ID,ORDER_ID,ORDER_NOMINEE_ID,STAFF_ID,STARTS_AT,SERVICE_ENDS_AT,ENDS_AT,BLOCK_STATUS,CREATED_AT,UPDATED_AT)
            SELECT UUID(),ORDER_ID,ID,STAFF_ID,REQUESTED_STARTS_AT,REQUESTED_SERVICE_ENDS_AT,
                REQUESTED_SERVICE_ENDS_AT,'released',@Now,@Now FROM ORDER_NOMINEES WHERE ORDER_ID=@Id;
            INSERT INTO ORDER_FULFILLMENT_EVENTS
                (ID,ORDER_ID,UNIT_ID,EVENT_TYPE,OPERATION_ID,FROM_PERIOD_ID,TO_PERIOD_ID,
                 ACTUAL_STARTS_AT,ACTUAL_ENDS_AT,REST_MINUTES,COMPENSATION_AMOUNT,REASON,ACTOR_ID,CREATED_AT)
            SELECT UUID(),ORDER_ID,ID,'backfill',UUID(),BUSINESS_PERIOD_ID,FULFILLMENT_PERIOD_ID,
                ACTUAL_STARTS_AT,ACTUAL_ENDS_AT,0,0,@Reason,@ActorId,@Now
            FROM ORDER_FULFILLMENT_UNITS WHERE ORDER_ID=@Id;
            UPDATE SETTLEMENT_RUNS R JOIN CUSTOMER_ORDER_SESSIONS S ON S.BUSINESS_DATE=R.BUSINESS_DATE
            SET R.INPUT_VERSION=R.INPUT_VERSION+1,R.STATUS='draft'
            WHERE S.ID=@SessionId AND R.STATUS<>'finalized';
            """, new { order.Id, order.SessionId, backfill.OperationId, PayloadHash = order.BackfillPayloadHash,
            DuplicateHash = order.BackfillDuplicateHash, OccurredAt = occurredAt, Reason = backfill.Reason.Trim(),
            backfill.LinkedCashRecordId, Snapshot = JsonSerializer.Serialize(snapshot), Now = order.SubmittedAt,
            order.ActorId, End = end }, tx, cancellationToken: ct));
        await OrderingFinanceRepository.LinkBackfillCashAsync(connection, tx, order, ct);
        await InsertAuditAsync(connection, tx, order.Id, order.SessionId, "order.backfill_created", null,
            JsonSerializer.Serialize(new { snapshot, order.Items, order.Nominees, order.Rooms, order.Tips }), order.ActorId!,
            order.ActorRole ?? "staff", order.SubmittedAt, ct);
    }

    private sealed class BackfillOperation
    {
        public string OrderId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string PayloadHash { get; set; } = "";
    }
}
