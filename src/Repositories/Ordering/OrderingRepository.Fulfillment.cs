using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    private static async Task ValidateNewNominationEligibilityAsync(MySqlConnection connection,
        MySqlTransaction tx, NewOrderAggregate order, CancellationToken ct)
    {
        foreach (var nominee in order.Nominees.OrderBy(n => n.StaffId, StringComparer.Ordinal))
        {
            // Lock the approved plan and daily mode while deciding whether this new sale is allowed.
            var eligible = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
                SELECT CASE WHEN M.IS_ACTIVE=TRUE AND M.IS_NOMINATABLE=TRUE
                    AND P.IS_WORKING=TRUE AND P.APPROVAL_STATUS='approved'
                    AND @Now>=TIMESTAMP(P.BUSINESS_DATE,P.START_TIME)
                    AND @Now<TIMESTAMP(P.BUSINESS_DATE,P.END_TIME)
                        + INTERVAL (CASE WHEN P.END_TIME<=P.START_TIME THEN 1 ELSE 0 END) DAY
                    AND COALESCE(D.IS_WORKING,TRUE)=TRUE
                    AND COALESCE(D.STOP_ACCEPTING_NEW_ORDERS,FALSE)=FALSE THEN 1 ELSE 0 END
                FROM CUSTOMER_ORDER_SESSIONS S JOIN STAFF_MEMBERS M ON M.ID=@StaffId
                LEFT JOIN STAFF_DUTY_PLANS P ON P.STAFF_MEMBER_ID=M.ID AND P.BUSINESS_DATE=S.BUSINESS_DATE
                LEFT JOIN STAFF_DAILY_WORK_MODES D ON D.STAFF_MEMBER_ID=M.ID AND D.BUSINESS_DATE=S.BUSINESS_DATE
                WHERE S.ID=@SessionId FOR UPDATE
                """, new { nominee.StaffId, order.SessionId, Now=order.SubmittedAt }, tx, cancellationToken:ct));
            if (eligible != 1)
                throw new ConflictException("店員班表或接單狀態已變更，請重新選擇。", "NOMINATION_UNAVAILABLE");
        }
    }

    public async Task<OrderFulfillmentDto> GetFulfillmentAsync(string orderId, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await ReadFulfillmentAsync(connection, null, orderId, cancellationToken);
    }

    private static async Task<OrderFulfillmentDto> ReadFulfillmentAsync(MySqlConnection connection,
        MySqlTransaction? transaction, string orderId, CancellationToken ct)
    {
        var order = await connection.QuerySingleOrDefaultAsync<FulfillmentOrder>(new CommandDefinition("""
            SELECT ID AS Id, BUSINESS_PERIOD_ID AS BusinessPeriodId, FLOW_VERSION AS FlowVersion,
                   ORDER_STATUS AS OrderStatus, SESSION_ID AS SessionId
            FROM ORDERS WHERE ID=@OrderId;
            """, new { OrderId = orderId }, transaction, cancellationToken: ct))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        var units = await connection.QueryAsync<FulfillmentUnit>(new CommandDefinition(
            UnitSelect + " WHERE ORDER_ID=@OrderId ORDER BY CREATED_AT, ID;", new { OrderId = orderId }, transaction, cancellationToken: ct));
        return new OrderFulfillmentDto(orderId, order.BusinessPeriodId, order.FlowVersion, order.OrderStatus,
            units.Select(u => new FulfillmentUnitDto(u.Id, u.OrderItemId, u.NomineeId, u.Kind, u.Name, u.StaffId,
                u.Quantity, u.AcceptedQuantity, u.StartedQuantity, u.CompletedQuantity, u.CancelledQuantity,
                u.Status, u.Version, u.OriginalAmount, u.OriginalCredit, u.CancelledAmount, u.ReturnedCredit,
                u.PurchasedMinutes, Offset(u.ScheduledStartsAt), Offset(u.ScheduledEndsAt), Offset(u.ActualStartsAt),
                Offset(u.ActualEndsAt), Array.Empty<string>())).ToArray());
    }

    private static DateTimeOffset? Offset(DateTime? value)
        => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null;

    // This hook runs only after the immutable items and nominees have been inserted, in the same transaction.
    private static async Task InitializeFulfillmentAsync(MySqlConnection connection, MySqlTransaction tx,
        string orderId, string sessionId, DateTime now, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERS O JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=O.SESSION_ID
            LEFT JOIN BUSINESS_PERIODS P ON P.ID=S.BUSINESS_PERIOD_ID
            SET O.BUSINESS_PERIOD_ID=S.BUSINESS_PERIOD_ID, O.FLOW_VERSION=COALESCE(P.FLOW_VERSION,1)
            WHERE O.ID=@OrderId;
            """, new { OrderId = orderId }, tx, cancellationToken: ct));
        var order = await connection.QuerySingleAsync<FulfillmentOrder>(new CommandDefinition("""
            SELECT ID AS Id, BUSINESS_PERIOD_ID AS BusinessPeriodId, FLOW_VERSION AS FlowVersion,
                   MEAL_CREDIT_APPLIED AS MealCreditApplied, ORDER_STATUS AS OrderStatus
            FROM ORDERS WHERE ID=@OrderId;
            """, new { OrderId = orderId }, tx, cancellationToken: ct));
        if (order.FlowVersion < 2) return;
        var items = (await connection.QueryAsync<OrderItemRow>(new CommandDefinition("""
            SELECT ID AS Id, ITEM_TYPE AS ItemType, REFERENCE_ID AS ReferenceId, PARENT_ITEM_ID AS ParentItemId,
                   NAME_SNAPSHOT AS NameSnapshot, QUANTITY AS Quantity, LINE_TOTAL AS LineTotal
            FROM ORDER_ITEMS WHERE ORDER_ID=@OrderId ORDER BY SORT_ORDER,ID;
            """, new { OrderId = orderId }, tx, cancellationToken: ct))).ToArray();
        var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
            SELECT ID AS Id, STAFF_ID AS StaffId, STAFF_NAME_SNAPSHOT AS StaffNameSnapshot,
                   SERVICE_NAME_SNAPSHOT AS ServiceNameSnapshot, RESERVED_MINUTES AS ReservedMinutes,
                   REQUESTED_STARTS_AT AS RequestedStartsAt, REQUESTED_SERVICE_ENDS_AT AS RequestedServiceEndsAt
            FROM ORDER_NOMINEES WHERE ORDER_ID=@OrderId;
            """, new { OrderId = orderId }, tx, cancellationToken: ct))).ToArray();
        var units = new List<FulfillmentUnit>();
        var mealTotal = items.Where(i => i.ItemType is "menu_item" or "menu_set").Sum(i => (long)i.LineTotal);
        long mealCumulative = 0;
        int creditAllocated = 0;
        foreach (var item in items.Where(i => i.ParentItemId is null))
        {
            var unit = new FulfillmentUnit { Id = NewId(), OrderItemId = item.Id, Name = item.NameSnapshot,
                Quantity = 1, OriginalAmount = item.LineTotal, Kind = "other", Status = "waiting" };
            if (item.ItemType is "menu_item" or "menu_set")
            {
                unit.Kind = "meal";
                unit.Quantity = item.Quantity;
                mealCumulative += item.LineTotal;
                var cumulativeCredit = mealTotal == 0 ? 0 : (int)(mealCumulative * order.MealCreditApplied / mealTotal);
                unit.OriginalCredit = cumulativeCredit - creditAllocated;
                creditAllocated = cumulativeCredit;
            }
            else if (item.ItemType == "nomination_base")
            {
                var nominee = nominees.Single(n => n.StaffId == item.ReferenceId);
                unit.Kind = "nominee";
                unit.NomineeId = nominee.Id;
                unit.StaffId = nominee.StaffId;
                unit.Name = $"{nominee.StaffNameSnapshot}｜{nominee.ServiceNameSnapshot}";
                unit.PurchasedMinutes = nominee.ReservedMinutes;
                unit.ScheduledStartsAt = nominee.RequestedStartsAt;
                unit.ScheduledEndsAt = nominee.RequestedServiceEndsAt;
                unit.OriginalAmount += items.Where(i => i.ParentItemId == item.Id).Sum(i => i.LineTotal);
            }
            else if (item.ItemType == "staff_service_addon")
            {
                var addon = await connection.QuerySingleAsync<FulfillmentUnit>(new CommandDefinition("""
                    SELECT ID AS RelatedId, STAFF_ID AS StaffId, SERVICE_DURATION_MINUTES AS PurchasedMinutes,
                           ADDON_STATUS AS Status FROM ORDER_SERVICE_ADDONS WHERE ORDER_ID=@OrderId;
                    """, new { OrderId = orderId }, tx, cancellationToken: ct));
                unit.Kind = "addon"; unit.RelatedId = addon.RelatedId; unit.StaffId = addon.StaffId;
                unit.PurchasedMinutes = addon.PurchasedMinutes;
                if (addon.Status == "confirmed") { unit.AcceptedQuantity = 1; unit.Status = "accepted"; }
            }
            else if (item.ItemType == "room_service")
            {
                var room = await connection.QuerySingleAsync<FulfillmentUnit>(new CommandDefinition("""
                    SELECT ID AS RelatedId, STARTS_AT AS ScheduledStartsAt, ENDS_AT AS ScheduledEndsAt
                    FROM ROOM_SERVICE_ORDERS WHERE ORDER_ITEM_ID=@ItemId;
                    """, new { ItemId = item.Id }, tx, cancellationToken: ct));
                unit.Kind = "room"; unit.RelatedId = room.RelatedId;
                unit.ScheduledStartsAt = room.ScheduledStartsAt; unit.ScheduledEndsAt = room.ScheduledEndsAt;
            }
            else if (item.ItemType == "tip")
            {
                // Tips are financial allocations, with no separate service promise to block closing.
                unit.Kind = "tip"; unit.Status = "completed";
                unit.AcceptedQuantity = unit.StartedQuantity = unit.CompletedQuantity = 1;
            }
            units.Add(unit);
        }
        foreach (var unit in units)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ORDER_FULFILLMENT_UNITS
                (ID,ORDER_ID,BUSINESS_PERIOD_ID,ORDER_ITEM_ID,NOMINEE_ID,RELATED_ID,KIND,NAME_SNAPSHOT,STAFF_ID,
                 QUANTITY,ACCEPTED_QUANTITY,STARTED_QUANTITY,COMPLETED_QUANTITY,UNIT_STATUS,ORIGINAL_AMOUNT,ORIGINAL_CREDIT,
                 PURCHASED_MINUTES,SCHEDULED_STARTS_AT,SCHEDULED_ENDS_AT,CREATED_AT,UPDATED_AT)
                VALUES (@Id,@OrderId,@BusinessPeriodId,@OrderItemId,@NomineeId,@RelatedId,@Kind,@Name,@StaffId,
                 @Quantity,@AcceptedQuantity,@StartedQuantity,@CompletedQuantity,@Status,@OriginalAmount,@OriginalCredit,
                 @PurchasedMinutes,@ScheduledStartsAt,@ScheduledEndsAt,@Now,@Now);
                """, new { unit.Id, OrderId = orderId, order.BusinessPeriodId, unit.OrderItemId, unit.NomineeId,
                unit.RelatedId, unit.Kind, unit.Name, unit.StaffId, unit.Quantity, unit.AcceptedQuantity,
                unit.StartedQuantity, unit.CompletedQuantity, unit.Status, unit.OriginalAmount, unit.OriginalCredit,
                unit.PurchasedMinutes, unit.ScheduledStartsAt, unit.ScheduledEndsAt, Now = now }, tx, cancellationToken: ct));
        }
        if (units.Count > 0 && units.All(unit => unit.Status == "completed"))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ORDERS SET ORDER_STATUS='completed',COMPLETED_AT=@Now,UPDATED_AT=@Now WHERE ID=@OrderId",
                new { OrderId=orderId, Now=now }, tx, cancellationToken:ct));
            await InsertHistoryAsync(connection, tx, orderId, order.OrderStatus, "completed",
                "此單只有小費，無待履約項目。", "system", null, now, ct);
        }
    }

    public async Task<OrderFulfillmentDto> TransitionFulfillmentAsync(string orderId, string unitId,
        FulfillmentTransitionRequest request, string actorId, string actorRole, string? staffId,
        DateTime now, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { unitId, request.Action, request.Quantity, request.Reason, request.ScheduledStartsAt, request.ExpectedVersion }))));
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var sessionId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT SESSION_ID FROM ORDERS WHERE ID=@OrderId;", new { OrderId = orderId }, tx, cancellationToken: ct))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT ID FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId FOR UPDATE;", new { SessionId = sessionId }, tx, cancellationToken: ct));
        var order = await connection.QuerySingleAsync<FulfillmentOrder>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, FLOW_VERSION AS FlowVersion,
                   BUSINESS_PERIOD_ID AS BusinessPeriodId, ORDER_STATUS AS OrderStatus
            FROM ORDERS WHERE ID=@OrderId FOR UPDATE;
            """, new { OrderId = orderId }, tx, cancellationToken: ct));
        if (order.FlowVersion < 2) throw new BusinessException("此歷史訂單沿用整單操作。", "FULFILLMENT_LEGACY_ORDER");
        var previous = await connection.QuerySingleOrDefaultAsync<FulfillmentOperation>(new CommandDefinition("""
            SELECT PAYLOAD_HASH AS PayloadHash, RESULT_JSON AS ResultJson FROM ORDER_FULFILLMENT_OPERATIONS
            WHERE ORDER_ID=@OrderId AND OPERATION_ID=@OperationId;
            """, new { OrderId = orderId, request.OperationId }, tx, cancellationToken: ct));
        var unit = await connection.QuerySingleOrDefaultAsync<FulfillmentUnit>(new CommandDefinition(
            UnitSelect + " WHERE ORDER_ID=@OrderId AND ID=@UnitId FOR UPDATE;", new { OrderId = orderId, UnitId = unitId }, tx, cancellationToken: ct))
            ?? throw new BusinessException("找不到履約項目。", "FULFILLMENT_UNIT_NOT_FOUND");
        if (unit.StaffId is not null && actorRole is not (AdminRole.Manager or AdminRole.Developer) && unit.StaffId != staffId)
            throw new ForbiddenException("只有被指名店員本人或經理可處理這項服務。", "FULFILLMENT_STAFF_SCOPE");
        if (previous is not null)
        {
            if (previous.PayloadHash != payloadHash) throw new ConflictException("此操作識別碼已用於不同內容。", "OPERATION_PAYLOAD_CONFLICT");
            await tx.CommitAsync(ct);
            return JsonSerializer.Deserialize<OrderFulfillmentDto>(previous.ResultJson)!;
        }
        if (unit.Version != request.ExpectedVersion) throw new ConflictException("其他店員已更新此項目，請重新載入後操作。", "FULFILLMENT_VERSION_CONFLICT");
        var before = JsonSerializer.Serialize(unit);
        var q = request.Quantity;
        var oldCancelled = unit.CancelledQuantity;
        switch (request.Action)
        {
            case "accept" when q <= unit.Quantity - unit.CancelledQuantity - unit.AcceptedQuantity:
                unit.AcceptedQuantity += q;
                break;
            case "start" when q <= unit.AcceptedQuantity - unit.StartedQuantity:
                unit.StartedQuantity += q; unit.ActualStartsAt ??= now;
                break;
            case "complete" when q <= unit.StartedQuantity - unit.CompletedQuantity:
                if (unit.Kind is "nominee" or "addon" && unit.ScheduledEndsAt > now && request.Reason is null)
                    throw new BusinessException("提早完成請填寫原因；金額不會自動減少。", "EARLY_COMPLETION_REASON_REQUIRED");
                unit.CompletedQuantity += q; unit.ActualEndsAt = now;
                break;
            case "cancel" when q <= unit.Quantity - unit.CancelledQuantity - unit.StartedQuantity:
                unit.CancelledQuantity += q;
                unit.AcceptedQuantity = Math.Min(unit.AcceptedQuantity, unit.Quantity - unit.CancelledQuantity);
                break;
            case "reschedule" when unit.Kind == "nominee" && unit.StartedQuantity == 0 && unit.CancelledQuantity == 0:
                if (!request.ScheduledStartsAt.HasValue) throw new BusinessException("請選擇新開始時間。", "FULFILLMENT_SCHEDULE_REQUIRED");
                unit.ScheduledStartsAt = request.ScheduledStartsAt.Value.ToOffset(TimeSpan.FromHours(8)).DateTime;
                if (unit.ScheduledStartsAt < now) throw new BusinessException("新開始時間不可早於目前時間。", "NOMINATION_START_IN_PAST");
                unit.ScheduledEndsAt = unit.ScheduledStartsAt.Value.AddMinutes(unit.PurchasedMinutes);
                unit.AcceptedQuantity = 0;
                break;
            default:
                throw new BusinessException("目前進度無法執行此數量，請重新確認。", "FULFILLMENT_TRANSITION_INVALID");
        }
        if (unit.Kind == "nominee") await ApplyNomineeFulfillmentAsync(connection, tx, order, unit, request.Action, actorId, now, ct);
        if (unit.Kind == "addon") await ApplyAddonFulfillmentAsync(connection, tx, unit, request.Action, actorId, now, ct);
        if (unit.Kind == "room")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ROOM_SERVICE_ORDERS SET ORDER_STATUS=@Status,UPDATED_AT=@Now WHERE ID=@RelatedId;
                """, new { unit.RelatedId, Now = now, Status = request.Action switch { "start" => "in_service", "complete" => "completed", "cancel" => "cancelled", _ => "scheduled" } }, tx, cancellationToken: ct));
        }
        unit.Status = unit.CancelledQuantity == unit.Quantity ? "cancelled"
            : unit.CompletedQuantity + unit.CancelledQuantity == unit.Quantity ? "completed"
            : unit.StartedQuantity > unit.CompletedQuantity ? "in_service"
            : unit.AcceptedQuantity + unit.CancelledQuantity == unit.Quantity ? "accepted" : "waiting";
        if (unit.CancelledQuantity > oldCancelled)
        {
            var cancelledAmount = (int)((long)unit.OriginalAmount * unit.CancelledQuantity / unit.Quantity);
            var returnedCredit = (int)((long)unit.OriginalCredit * unit.CancelledQuantity / unit.Quantity);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ORDERS SET SUBTOTAL=SUBTOTAL-@Amount,MEAL_CREDIT_APPLIED=MEAL_CREDIT_APPLIED-@Credit,
                    TOTAL_AMOUNT=TOTAL_AMOUNT-@Amount+@Credit WHERE ID=@OrderId;
                UPDATE CUSTOMER_ORDER_SESSIONS SET REMAINING_MEAL_CREDIT=REMAINING_MEAL_CREDIT+@Credit,
                    UPDATED_AT=@Now WHERE ID=@SessionId;
                """, new { OrderId = orderId, SessionId = sessionId, Amount = cancelledAmount - unit.CancelledAmount,
                    Credit = returnedCredit - unit.ReturnedCredit, Now = now }, tx, cancellationToken: ct));
            unit.CancelledAmount = cancelledAmount; unit.ReturnedCredit = returnedCredit;
        }
        unit.Version++;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDER_FULFILLMENT_UNITS SET ACCEPTED_QUANTITY=@AcceptedQuantity,STARTED_QUANTITY=@StartedQuantity,
                COMPLETED_QUANTITY=@CompletedQuantity,CANCELLED_QUANTITY=@CancelledQuantity,UNIT_STATUS=@Status,
                CANCELLED_AMOUNT=@CancelledAmount,RETURNED_CREDIT=@ReturnedCredit,SCHEDULED_STARTS_AT=@ScheduledStartsAt,
                SCHEDULED_ENDS_AT=@ScheduledEndsAt,ACTUAL_STARTS_AT=@ActualStartsAt,ACTUAL_ENDS_AT=@ActualEndsAt,
                VERSION=@Version,UPDATED_AT=@Now WHERE ID=@Id;
            """, new { unit.Id, unit.AcceptedQuantity, unit.StartedQuantity, unit.CompletedQuantity, unit.CancelledQuantity,
                unit.Status, unit.CancelledAmount, unit.ReturnedCredit, unit.ScheduledStartsAt, unit.ScheduledEndsAt,
                unit.ActualStartsAt, unit.ActualEndsAt, unit.Version, Now = now }, tx, cancellationToken: ct));
        var data = await ReadFulfillmentAsync(connection, tx, orderId, ct);
        var active = data.Units.Where(u => u.Status != "cancelled").ToArray();
        var nextStatus = active.Length == 0 ? "cancelled" : active.All(u => u.Status == "completed") ? "completed"
            : active.Any(u => u.StartedQuantity > 0) ? "in_service"
            : active.Any(u => u.Kind is "nominee" or "addon" && u.AcceptedQuantity == 0)
                ? (active.Any(u => u.AcceptedQuantity > 0 && u.Kind != "tip") ? "partially_confirmed" : "submitted") : "confirmed";
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDERS SET ORDER_STATUS=@Status,UPDATED_AT=@Now,UPDATED_BY=@ActorId,
                STORE_CONFIRMATION_STATUS=CASE WHEN STORE_CONFIRMATION_STATUS='pending' THEN 'approved' ELSE STORE_CONFIRMATION_STATUS END,
                STORE_CONFIRMED_AT=CASE WHEN STORE_CONFIRMATION_STATUS='approved' THEN COALESCE(STORE_CONFIRMED_AT,@Now) ELSE STORE_CONFIRMED_AT END,
                STORE_CONFIRMED_BY=CASE WHEN STORE_CONFIRMATION_STATUS='approved' THEN COALESCE(STORE_CONFIRMED_BY,@ActorId) ELSE STORE_CONFIRMED_BY END,
                CONFIRMED_AT=CASE WHEN @Status IN ('confirmed','in_service','completed') THEN COALESCE(CONFIRMED_AT,@Now) ELSE CONFIRMED_AT END,
                STARTED_AT=CASE WHEN @Status='in_service' THEN COALESCE(STARTED_AT,@Now) ELSE STARTED_AT END,
                COMPLETED_AT=CASE WHEN @Status='completed' THEN @Now ELSE NULL END,
                CANCELLED_AT=CASE WHEN @Status='cancelled' THEN @Now ELSE NULL END WHERE ID=@OrderId;
            """, new { Status = nextStatus, Now = now, ActorId = actorId, OrderId = orderId }, tx, cancellationToken: ct));
        await InsertHistoryAsync(connection, tx, orderId, order.OrderStatus, nextStatus,
            $"{unit.Name}：{request.Action} × {q}。{request.Reason}", "staff", actorId, now, ct);
        await InsertAuditAsync(connection, tx, orderId, sessionId, $"fulfillment.{request.Action}", before,
            JsonSerializer.Serialize(new { unit, request.OperationId }), actorId, actorRole, now, ct);
        data = data with { OrderStatus = nextStatus };
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDER_FULFILLMENT_OPERATIONS (ORDER_ID,OPERATION_ID,UNIT_ID,PAYLOAD_HASH,RESULT_JSON,ACTOR_ID,CREATED_AT)
            VALUES (@OrderId,@OperationId,@UnitId,@PayloadHash,@ResultJson,@ActorId,@Now);
            """, new { OrderId = orderId, request.OperationId, UnitId = unitId, PayloadHash = payloadHash,
                ResultJson = JsonSerializer.Serialize(data), ActorId = actorId, Now = now }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return data;
    }

    private static async Task ApplyNomineeFulfillmentAsync(MySqlConnection connection, MySqlTransaction tx,
        FulfillmentOrder order, FulfillmentUnit unit, string action, string actorId, DateTime now, CancellationToken ct)
    {
        var nominee = await connection.QuerySingleAsync<OrderNomineeRow>(new CommandDefinition("""
            SELECT ID AS Id, ORDER_ID AS OrderId,STAFF_ID AS StaffId,STAFF_NAME_SNAPSHOT AS StaffNameSnapshot,
                   REQUESTED_STARTS_AT AS RequestedStartsAt,REQUESTED_SERVICE_ENDS_AT AS RequestedServiceEndsAt,
                   REQUESTED_BUSY_UNTIL AS RequestedBusyUntil,BUFFER_MINUTES_SNAPSHOT AS BufferMinutesSnapshot,
                   CONFIRMATION_STATUS AS ConfirmationStatus FROM ORDER_NOMINEES WHERE ID=@NomineeId FOR UPDATE;
            """, new { unit.NomineeId }, tx, cancellationToken: ct));
        await LockStaffRowsAsync(connection, tx, [nominee.StaffId], ct);
        if (action is "cancel" or "complete" or "reschedule")
        {
            var hasAddons = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A JOIN ORDERS O ON O.ID=A.ORDER_ID
                WHERE A.PARENT_NOMINEE_ID=@NomineeId AND O.ORDER_STATUS NOT IN ('cancelled','rejected','expired','completed'));
                """, new { unit.NomineeId }, tx, cancellationToken: ct));
            if (hasAddons) throw new BusinessException("請先完成或取消此指名尚未處理的加購，再變更原服務。", "FULFILLMENT_ADDONS_PENDING");
        }
        if (action == "reschedule")
        {
            var period = await connection.QuerySingleOrDefaultAsync<BusinessPeriodRow>(new CommandDefinition("""
                SELECT PERIOD_STATUS AS PeriodStatus, ACTUAL_OPENED_AT AS ActualOpenedAt,
                       PROJECTED_CLOSE_AT AS ProjectedCloseAt FROM BUSINESS_PERIODS WHERE ID=@BusinessPeriodId;
                """, new { order.BusinessPeriodId }, tx, cancellationToken: ct));
            if (period is null || period.PeriodStatus is not ("open" or "coordination") ||
                unit.ScheduledEndsAt!.Value.AddMinutes(nominee.BufferMinutesSnapshot) > (period.ActualOpenedAt ?? now).AddHours(48))
                throw new BusinessException("請在仍營業的原營業期內重新安排；跨期服務將於後續階段提供。", "FULFILLMENT_PERIOD_CLOSED");
            nominee.RequestedStartsAt = unit.ScheduledStartsAt!.Value;
            nominee.RequestedServiceEndsAt = unit.ScheduledEndsAt.Value;
            nominee.RequestedBusyUntil = nominee.RequestedServiceEndsAt.AddMinutes(nominee.BufferMinutesSnapshot);
        }
        if (action is "accept" or "reschedule")
        {
            if (nominee.RequestedStartsAt < now)
                throw new BusinessException("預定開始时间已過，請使用此項目的改期入口重新安排。", "NOMINATION_START_IN_PAST");
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM STAFF_BUSY_BLOCKS WHERE STAFF_ID=@StaffId AND BLOCK_STATUS='active'
                    AND ORDER_NOMINEE_ID<>@NomineeId AND ENDS_AT>@StartsAt AND STARTS_AT<@EndsAt);
                """, new { nominee.StaffId, NomineeId = nominee.Id, StartsAt = nominee.RequestedStartsAt,
                    EndsAt = nominee.RequestedBusyUntil }, tx, cancellationToken: ct)))
                throw new ConflictException("此店員的新時段與已成立服務衝突，請改選時段。", "STAFF_TIME_CONFLICT");
        }
        if (action == "start" && await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM ORDER_FULFILLMENT_UNITS WHERE STAFF_ID=@StaffId AND KIND='nominee'
                AND ID<>@Id AND STARTED_QUANTITY>COMPLETED_QUANTITY);
            """, new { unit.StaffId, unit.Id }, tx, cancellationToken: ct)))
            throw new ConflictException("此店員仍有服務進行中，請先處理目前服務。", "STAFF_ALREADY_IN_SERVICE");
        if (action is "cancel" or "reschedule")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE STAFF_BUSY_BLOCKS SET BLOCK_STATUS='released',UPDATED_AT=@Now WHERE ORDER_NOMINEE_ID=@NomineeId AND BLOCK_STATUS='active';
                """, new { unit.NomineeId, Now = now }, tx, cancellationToken: ct));
        }
        if (action == "complete")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE STAFF_BUSY_BLOCKS SET SERVICE_ENDS_AT=@Now,ENDS_AT=@BusyUntil,UPDATED_AT=@Now
                WHERE ORDER_NOMINEE_ID=@NomineeId AND BLOCK_STATUS='active';
                """, new { unit.NomineeId, Now = now, BusyUntil = now.AddMinutes(nominee.BufferMinutesSnapshot) }, tx, cancellationToken: ct));
        }
        var confirmation = action switch { "cancel" => "cancelled", "reschedule" => "waiting", "complete" => "completed", _ => "confirmed" };
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDER_NOMINEES SET CONFIRMATION_STATUS=@Confirmation,
                CONFIRMED_AT=CASE WHEN @Action='accept' THEN @Now WHEN @Action='reschedule' THEN NULL ELSE CONFIRMED_AT END,
                CONFIRMED_BY=CASE WHEN @Action='accept' THEN @ActorId WHEN @Action='reschedule' THEN NULL ELSE CONFIRMED_BY END,
                REQUESTED_STARTS_AT=@RequestedStartsAt,REQUESTED_SERVICE_ENDS_AT=@RequestedServiceEndsAt,
                REQUESTED_BUSY_UNTIL=@RequestedBusyUntil,UPDATED_AT=@Now WHERE ID=@Id;
            """, new { nominee.Id, Confirmation = confirmation, Action = action, Now = now, ActorId = actorId,
                nominee.RequestedStartsAt, nominee.RequestedServiceEndsAt, nominee.RequestedBusyUntil }, tx, cancellationToken: ct));
        if (action == "accept")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO STAFF_BUSY_BLOCKS (ID,ORDER_ID,ORDER_NOMINEE_ID,STAFF_ID,STARTS_AT,SERVICE_ENDS_AT,ENDS_AT,BLOCK_STATUS,CREATED_AT,UPDATED_AT)
                VALUES (@Id,@OrderId,@NomineeId,@StaffId,@StartsAt,@ServiceEndsAt,@EndsAt,'active',@Now,@Now);
                """, new { Id = NewId(), OrderId = order.Id, NomineeId = nominee.Id, nominee.StaffId,
                    StartsAt = nominee.RequestedStartsAt, ServiceEndsAt = nominee.RequestedServiceEndsAt,
                    EndsAt = nominee.RequestedBusyUntil, Now = now }, tx, cancellationToken: ct));
            nominee.ConfirmationStatus = "confirmed";
            await MenuNotifications.EnsureNominationSchedulesAsync(connection, tx, [nominee], now, ct, nextScheduleRevision: true);
        }
        else
        {
            await MenuNotifications.InvalidateNominationSchedulesAsync(connection, tx, order.Id, now, ct,
                nomineeId: nominee.Id, reason: $"fulfillment_{action}",
                preserveRuleTypes: action == "start" ? new[] { "nomination_ending", "nomination_ended" } : Array.Empty<string>());
        }
    }

    private static async Task ApplyAddonFulfillmentAsync(MySqlConnection connection, MySqlTransaction tx,
        FulfillmentUnit unit, string action, string actorId, DateTime now, CancellationToken ct)
    {
        if (action is "accept" or "start")
        {
            var valid = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A JOIN ORDER_NOMINEES N ON N.ID=A.PARENT_NOMINEE_ID
                    LEFT JOIN ORDER_FULFILLMENT_UNITS F ON F.NOMINEE_ID=N.ID
                    WHERE A.ID=@RelatedId AND N.CONFIRMATION_STATUS='confirmed'
                    AND (F.ID IS NULL OR F.UNIT_STATUS IN ('accepted','in_service'))
                    AND DATE_ADD(GREATEST(N.REQUESTED_STARTS_AT,@Now),INTERVAL A.SERVICE_DURATION_MINUTES MINUTE)<=N.REQUESTED_SERVICE_ENDS_AT);
                """, new { unit.RelatedId, Now = now }, tx, cancellationToken: ct));
            if (!valid) throw new BusinessException("原服務剩餘時間或進度已變更，請先協調加購。", "ADDON_PARENT_INACTIVE");
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDER_SERVICE_ADDONS SET ADDON_STATUS=@Status,UPDATED_AT=@Now,
                CONFIRMED_AT=CASE WHEN @Action='accept' THEN @Now ELSE CONFIRMED_AT END,
                CONFIRMED_BY=CASE WHEN @Action='accept' THEN @ActorId ELSE CONFIRMED_BY END WHERE ID=@RelatedId;
            """, new { unit.RelatedId, Action = action, ActorId = actorId, Now = now,
                Status = action switch { "accept" => "confirmed", "start" => "in_service", "complete" => "completed", _ => "cancelled" } }, tx, cancellationToken: ct));
    }

    private static async Task EnsureLegacyOrderMutationAsync(MySqlConnection connection, MySqlTransaction tx,
        string orderId, CancellationToken ct)
    {
        var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT FLOW_VERSION FROM ORDERS WHERE ID=@OrderId FOR UPDATE;", new { OrderId = orderId }, tx, cancellationToken: ct));
        if (version >= 2) throw new ConflictException("此訂單已使用分項履約，請在各餐點或服務項目操作。", "ORDER_REQUIRES_FULFILLMENT");
    }

    private const string UnitSelect = """
        SELECT ID AS Id, ORDER_ITEM_ID AS OrderItemId, NOMINEE_ID AS NomineeId,RELATED_ID AS RelatedId,
            KIND AS Kind, NAME_SNAPSHOT AS Name, STAFF_ID AS StaffId, QUANTITY AS Quantity,
            ACCEPTED_QUANTITY AS AcceptedQuantity,STARTED_QUANTITY AS StartedQuantity,COMPLETED_QUANTITY AS CompletedQuantity,
            CANCELLED_QUANTITY AS CancelledQuantity,UNIT_STATUS AS Status,VERSION AS Version,
            ORIGINAL_AMOUNT AS OriginalAmount,ORIGINAL_CREDIT AS OriginalCredit,CANCELLED_AMOUNT AS CancelledAmount,
            RETURNED_CREDIT AS ReturnedCredit,PURCHASED_MINUTES AS PurchasedMinutes,SCHEDULED_STARTS_AT AS ScheduledStartsAt,
            SCHEDULED_ENDS_AT AS ScheduledEndsAt,ACTUAL_STARTS_AT AS ActualStartsAt,ACTUAL_ENDS_AT AS ActualEndsAt
        FROM ORDER_FULFILLMENT_UNITS
        """;

    private sealed class FulfillmentUnit
    {
        public string Id { get; set; } = "";
        public string? OrderItemId { get; set; }
        public string? NomineeId { get; set; }
        public string? RelatedId { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string? StaffId { get; set; }
        public int Quantity { get; set; }
        public int AcceptedQuantity { get; set; }
        public int StartedQuantity { get; set; }
        public int CompletedQuantity { get; set; }
        public int CancelledQuantity { get; set; }
        public string Status { get; set; } = "";
        public int Version { get; set; }
        public int OriginalAmount { get; set; }
        public int OriginalCredit { get; set; }
        public int CancelledAmount { get; set; }
        public int ReturnedCredit { get; set; }
        public int PurchasedMinutes { get; set; }
        public DateTime? ScheduledStartsAt { get; set; }
        public DateTime? ScheduledEndsAt { get; set; }
        public DateTime? ActualStartsAt { get; set; }
        public DateTime? ActualEndsAt { get; set; }
    }
    private sealed class FulfillmentOrder
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string? BusinessPeriodId { get; set; }
        public int FlowVersion { get; set; }
        public int MealCreditApplied { get; set; }
        public string OrderStatus { get; set; } = "";
    }
    private sealed class FulfillmentOperation
    {
        public string PayloadHash { get; set; } = "";
        public string ResultJson { get; set; } = "";
    }
}
