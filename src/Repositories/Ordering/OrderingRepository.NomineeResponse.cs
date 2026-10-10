using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    public async Task<IReadOnlyList<AdminOrderSessionRow>> GetDesignatedSessionsAsync(DateOnly businessDate,
        string staffId, CancellationToken cancellationToken)
    {
        // Include only current participation, including an earlier order fulfilled in this period.
        // Customer identity/history is deliberately not used to expand the session scope.
        const string sql = $"""
            SELECT {SessionColumns}, COUNT(O.ID) AS OrderCount,
                SUM(CASE WHEN O.ORDER_STATUS IN ('submitted','needs_reschedule','partially_confirmed') THEN 1 ELSE 0 END) AS WaitingOrderCount,
                SUM(CASE WHEN O.ORDER_STATUS IN ('confirmed','in_service','completed') THEN 1 ELSE 0 END) AS ConfirmedOrderCount,
                COALESCE(SUM(O.TOTAL_AMOUNT),0) AS TotalAmount, MAX(O.SUBMITTED_AT) AS LastOrderedAt
            FROM CUSTOMER_ORDER_SESSIONS S LEFT JOIN ORDERS O ON O.SESSION_ID=S.ID
            WHERE (S.BUSINESS_DATE=@BusinessDate AND EXISTS(
                SELECT 1 FROM ORDERS R WHERE R.SESSION_ID=S.ID AND (
                    EXISTS(SELECT 1 FROM ORDER_NOMINEES N WHERE N.ORDER_ID=R.ID AND N.STAFF_ID=@StaffId)
                    OR EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A WHERE A.ORDER_ID=R.ID AND A.STAFF_ID=@StaffId))))
                OR EXISTS(SELECT 1 FROM ORDER_FULFILLMENT_UNITS F
                    JOIN ORDERS R ON R.ID=F.ORDER_ID
                    JOIN BUSINESS_PERIODS P ON P.ID=COALESCE(F.FULFILLMENT_PERIOD_ID,F.BUSINESS_PERIOD_ID)
                    WHERE R.SESSION_ID=S.ID AND F.STAFF_ID=@StaffId AND P.BUSINESS_DATE=@BusinessDate
                    AND F.KIND IN ('nominee','addon') AND F.UNIT_STATUS NOT IN ('cancelled','carried_forward'))
            GROUP BY S.ID ORDER BY LastOrderedAt DESC;
            """;
        return await QueryAsync<AdminOrderSessionRow>(sql, new { BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), StaffId = staffId }, cancellationToken);
    }

    public async Task DeclineLegacyNomineeAsync(string orderId, string nomineeId, string staffId,
        string actorId, string actorRole, string reason, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureLegacyOrderMutationAsync(connection, tx, orderId, cancellationToken);
        var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, ORDER_STATUS AS OrderStatus,
                   STORE_CONFIRMATION_STATUS AS StoreConfirmationStatus
            FROM ORDERS WHERE ID=@OrderId FOR UPDATE;
            """, new { OrderId = orderId }, tx, cancellationToken: cancellationToken))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        var nominee = await connection.QuerySingleOrDefaultAsync<OrderNomineeRow>(new CommandDefinition("""
            SELECT ID AS Id, STAFF_ID AS StaffId, CONFIRMATION_STATUS AS ConfirmationStatus
            FROM ORDER_NOMINEES WHERE ID=@NomineeId AND ORDER_ID=@OrderId FOR UPDATE;
            """, new { NomineeId = nomineeId, OrderId = orderId }, tx, cancellationToken: cancellationToken));
        if (nominee is null || nominee.StaffId != staffId)
            throw new ForbiddenException("只有收到請求的指名人員本人可以回應。", "NOMINEE_RESPONSE_SCOPE");
        if (nominee.ConfirmationStatus == "needs_coordination")
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }
        if (order.StoreConfirmationStatus == "pending" || nominee.ConfirmationStatus != "waiting" ||
            order.OrderStatus is not ("submitted" or "partially_confirmed"))
            throw new ConflictException("此指名請求已更新，請重新整理。", "NOMINEE_RESPONSE_STATE_CONFLICT");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDER_NOMINEES SET CONFIRMATION_STATUS='needs_coordination', UPDATED_AT=@Now WHERE ID=@NomineeId;
            UPDATE ORDERS SET ORDER_STATUS='needs_reschedule', UPDATED_AT=@Now, UPDATED_BY=@ActorId WHERE ID=@OrderId;
            """, new { NomineeId = nomineeId, OrderId = orderId, ActorId = actorId, Now = now }, tx, cancellationToken: cancellationToken));
        await MenuNotifications.InvalidateNominationSchedulesAsync(connection, tx, orderId, now,
            cancellationToken, nomineeId: nomineeId, reason: "nominee_declined");
        await InsertHistoryAsync(connection, tx, orderId, order.OrderStatus, "needs_reschedule",
            $"指名人員無法承接，交回經理協調：{reason}", "staff", actorId, now, cancellationToken);
        await InsertAuditAsync(connection, tx, orderId, order.SessionId, "decline_nominee",
            "waiting", "needs_coordination", actorId, actorRole, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
