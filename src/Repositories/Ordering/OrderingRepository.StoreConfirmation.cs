using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    private static async Task DecideFulfillmentStoreConfirmationAsync(MySqlConnection connection,
        MySqlTransaction tx, OrderRow order, string decision, string? reason, string actorId,
        string actorRole, DateTime now, CancellationToken ct)
    {
        if (order.StoreConfirmationStatus != "pending" ||
            order.OrderStatus is not ("submitted" or "partially_confirmed" or "confirmed" or "needs_reschedule" or "in_service" or "completed"))
            throw new BusinessException("此訂單目前不需要店員協調確認。", "STORE_CONFIRMATION_NOT_PENDING");
        if (decision is not ("approved" or "rejected"))
            throw new BusinessException("請選擇接受或拒絕協調單。", "STORE_CONFIRMATION_DECISION_INVALID");
        if (decision == "rejected" && string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("拒絕協調單時必須填寫原因。", "STORE_CONFIRMATION_REASON_REQUIRED");

        // Locking reads are required after waiting for the session/order locks;
        // an earlier non-locking read may belong to an older MySQL snapshot.
        var units = (await connection.QueryAsync<FulfillmentUnit>(new CommandDefinition(
            UnitSelect + " WHERE ORDER_ID=@OrderId ORDER BY ID FOR UPDATE;",
            new { OrderId = order.Id }, tx, cancellationToken: ct))).ToArray();
        if (units.Length == 0)
            throw new ConflictException("此單的履約項目尚未完整建立，請重新讀取確認。", "FULFILLMENT_STORE_STATE_CONFLICT");
        var before = JsonSerializer.Serialize(new { Order = order, Units = units });
        var nextStatus = SummarizeFulfillmentStatus(units.Select(ToFulfillmentUnitDto).ToArray());
        var restoredCredit = 0;

        if (decision == "rejected")
        {
            restoredCredit = ValidateFulfillmentStoreRejection(units.Select(ToFulfillmentUnitDto).ToArray(), order.MealCreditApplied);
            await LockStaffRowsAsync(connection, tx,
                units.Where(u => u.Kind == "nominee" && u.StaffId is not null).Select(u => u.StaffId!), ct);
            var hasExternalAddons = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A
                JOIN ORDER_NOMINEES N ON N.ID=A.PARENT_NOMINEE_ID
                JOIN ORDERS O ON O.ID=A.ORDER_ID
                WHERE N.ORDER_ID=@OrderId AND A.ORDER_ID<>@OrderId
                  AND O.ORDER_STATUS NOT IN ('cancelled','rejected','expired','completed'));
                """, new { OrderId = order.Id }, tx, cancellationToken: ct));
            if (hasExternalAddons)
                throw new BusinessException("請先處理此指名尚未完成的加購，再拒絕協調單。", "FULFILLMENT_ADDONS_PENDING");

            var eventOrder = new FulfillmentOrder { Id = order.Id, SessionId = order.SessionId,
                BusinessPeriodId = order.BusinessPeriodId, FlowVersion = order.FlowVersion, OrderStatus = order.OrderStatus };
            foreach (var unit in units.Where(u => u.CancelledQuantity < u.Quantity))
            {
                var quantity = unit.Quantity - unit.CancelledQuantity;
                var version = unit.Version;
                unit.AcceptedQuantity = unit.StartedQuantity = unit.CompletedQuantity = 0;
                unit.CancelledQuantity = unit.Quantity;
                unit.CancelledAmount = unit.OriginalAmount;
                unit.ReturnedCredit = unit.OriginalCredit;
                unit.Status = "cancelled";
                unit.Version++;
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE ORDER_FULFILLMENT_UNITS SET ACCEPTED_QUANTITY=0,STARTED_QUANTITY=0,COMPLETED_QUANTITY=0,
                        CANCELLED_QUANTITY=QUANTITY,CANCELLED_AMOUNT=ORIGINAL_AMOUNT,RETURNED_CREDIT=ORIGINAL_CREDIT,
                        UNIT_STATUS='cancelled',VERSION=@Version,UPDATED_AT=@Now WHERE ID=@Id;
                    """, new { unit.Id, unit.Version, Now = now }, tx, cancellationToken: ct));
                await InsertFulfillmentEventAsync(connection, tx, eventOrder, unit, new FulfillmentTransitionRequest
                {
                    OperationId = Guid.NewGuid().ToString("D"), ExpectedVersion = version,
                    Action = "store_rejected", Quantity = quantity, Reason = reason!.Trim()
                }, actorId, now, ct);
            }
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ORDER_NOMINEES SET CONFIRMATION_STATUS='rejected',UPDATED_AT=@Now WHERE ORDER_ID=@OrderId;
                UPDATE ORDER_SERVICE_ADDONS SET ADDON_STATUS='rejected',UPDATED_AT=@Now WHERE ORDER_ID=@OrderId;
                UPDATE ROOM_SERVICE_ORDERS SET ORDER_STATUS='cancelled',UPDATED_AT=@Now
                    WHERE ORDER_ID=@OrderId AND ORDER_STATUS IN ('scheduled','in_service');
                UPDATE STAFF_BUSY_BLOCKS SET BLOCK_STATUS='released',UPDATED_AT=@Now
                    WHERE ORDER_ID=@OrderId AND BLOCK_STATUS='active';
                UPDATE CUSTOMER_ORDER_SESSIONS SET REMAINING_MEAL_CREDIT=REMAINING_MEAL_CREDIT+@Credit,
                    UPDATED_AT=@Now WHERE ID=@SessionId;
                """, new { OrderId = order.Id, order.SessionId, Credit = restoredCredit, Now = now }, tx, cancellationToken: ct));
            await MenuNotifications.InvalidateNominationSchedulesAsync(connection, tx, order.Id, now, ct,
                reason: "store_confirmation_rejected");
            nextStatus = "rejected";
        }

        // Approval only removes the store gate. It does not accept/start any
        // service or create a receipt. Rejection keeps the historical quote,
        // as in the legacy flow; rejected orders are excluded from receivables.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERS SET STORE_CONFIRMATION_STATUS=@Decision,STORE_CONFIRMED_AT=@Now,
                STORE_CONFIRMED_BY=@ActorId,ORDER_STATUS=@Status,UPDATED_AT=@Now,UPDATED_BY=@ActorId,
                CONFIRMED_AT=CASE WHEN @Status IN ('confirmed','in_service','completed') THEN COALESCE(CONFIRMED_AT,@Now) ELSE CONFIRMED_AT END,
                CANCELLED_AT=CASE WHEN @Decision='rejected' THEN @Now ELSE CANCELLED_AT END
            WHERE ID=@OrderId;
            """, new { OrderId = order.Id, Decision = decision, Status = nextStatus, Now = now, ActorId = actorId }, tx, cancellationToken: ct));
        await InsertHistoryAsync(connection, tx, order.Id, order.OrderStatus, nextStatus,
            decision == "rejected" ? reason!.Trim() : "店員已接受協調單，各服務仍依分項接待流程處理。",
            "staff", actorId, now, ct);
        await InsertAuditAsync(connection, tx, order.Id, order.SessionId, $"order.store_confirmation.{decision}", before,
            JsonSerializer.Serialize(new { decision, reason, restoredCredit, Units = units }), actorId, actorRole, now, ct);
    }

    private static string SummarizeFulfillmentStatus(IReadOnlyList<FulfillmentUnitDto> units)
    {
        var active = units.Where(u => u.Status != "cancelled").ToArray();
        return active.Length == 0 ? "cancelled" : active.All(u => u.Status == "completed") ? "completed"
            : active.Any(u => u.StartedQuantity > u.CompletedQuantity) ? "in_service"
            : active.Any(u => u.Status == "needs_coordination")
                ? (active.Any(u => u.AcceptedQuantity > 0 && u.Kind != "tip" && u.Status != "completed") ? "partially_confirmed" : "needs_reschedule")
            : active.Any(u => u.Kind is "nominee" or "addon" && u.AcceptedQuantity == 0)
                ? (active.Any(u => u.AcceptedQuantity > 0 && u.Kind != "tip") ? "partially_confirmed" : "submitted") : "confirmed";
    }

    private static int ValidateFulfillmentStoreRejection(IReadOnlyList<FulfillmentUnitDto> units, int remainingOrderCredit)
    {
        if (units.Count == 0 || remainingOrderCredit < 0 || units.Any(u =>
            u.Quantity < 1 || u.CancelledQuantity < 0 || u.CancelledQuantity > u.Quantity ||
            u.AcceptedQuantity < 0 || u.AcceptedQuantity > u.Quantity - u.CancelledQuantity ||
            u.StartedQuantity < 0 || u.StartedQuantity > u.AcceptedQuantity ||
            u.CompletedQuantity < 0 || u.CompletedQuantity > u.StartedQuantity ||
            u.OriginalAmount < 0 || u.CancelledAmount < 0 || u.CancelledAmount > u.OriginalAmount ||
            u.OriginalCredit < 0 || u.ReturnedCredit < 0 || u.ReturnedCredit > u.OriginalCredit ||
            (long)u.OriginalAmount * u.CancelledQuantity / u.Quantity != u.CancelledAmount ||
            (long)u.OriginalCredit * u.CancelledQuantity / u.Quantity != u.ReturnedCredit))
            throw new ConflictException("此單履約或折抵資料已變更，請重新讀取確認。", "FULFILLMENT_STORE_STATE_CONFLICT");
        // Tip completion is an automatic financial allocation, not delivery.
        if (units.Any(u => u.Kind != "tip" && (u.StartedQuantity > 0 || u.CompletedQuantity > 0)))
            throw new BusinessException("此單已有實際履約，請先接受協調單，再逐項處理未完成項目。", "FULFILLMENT_STORE_REJECTION_STARTED");
        var credit = units.Sum(u => (long)u.OriginalCredit - u.ReturnedCredit);
        if (credit != remainingOrderCredit)
            throw new ConflictException("此單剩餘折抵與履約紀錄不一致，請確認帳款後再處理。", "FULFILLMENT_STORE_STATE_CONFLICT");
        return remainingOrderCredit;
    }
}
