using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    private static bool IsOrderRepresentative(string role, string? staffId, IEnumerable<FulfillmentUnit> units)
        => role is AdminRole.Manager or AdminRole.Developer || staffId is not null &&
            units.Any(u => u.StaffId == staffId && u.CancelledQuantity < u.Quantity);

    public async Task<bool> CanRepresentOrderAsync(string orderId, string role, string? staffId, CancellationToken ct)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        return await CanRepresentOrderAsync(connection, null, orderId, role, staffId, ct);
    }

    private static async Task<bool> CanRepresentOrderAsync(MySqlConnection connection, MySqlTransaction? tx,
        string orderId, string role, string? staffId, CancellationToken ct)
    {
        if (role is AdminRole.Manager or AdminRole.Developer) return true;
        return staffId is not null && await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM ORDER_NOMINEES N WHERE N.ORDER_ID=@OrderId AND N.STAFF_ID=@StaffId
                AND N.CONFIRMATION_STATUS NOT IN ('cancelled','rejected'))
                OR EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A WHERE A.ORDER_ID=@OrderId AND A.STAFF_ID=@StaffId
                AND A.ADDON_STATUS NOT IN ('cancelled','rejected'))
                OR EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A JOIN ORDER_NOMINEES P ON P.ID=A.PARENT_NOMINEE_ID
                JOIN ORDER_NOMINEES R ON R.ORDER_ID=P.ORDER_ID WHERE A.ORDER_ID=@OrderId AND R.STAFF_ID=@StaffId
                AND R.CONFIRMATION_STATUS NOT IN ('cancelled','rejected'));

            """, new { OrderId = orderId, StaffId = staffId }, tx, cancellationToken: ct));
    }

    private static async Task RequireOrderRepresentativeAsync(MySqlConnection connection, MySqlTransaction? tx,
        string orderId, string role, string? staffId, CancellationToken ct)
    {
        if (!await CanRepresentOrderAsync(connection, tx, orderId, role, staffId, ct))
            throw new ForbiddenException("只有本單任一名有效指名人員或經理可以代辦整單服務。", "ORDER_SERVICE_SCOPE");
    }

    private static string ServiceSnapshot(FulfillmentOrder order, IReadOnlyList<FulfillmentUnit> units)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            order.Id, order.OrderStatus, order.StoreConfirmationStatus, order.Revision,
            Units = units.OrderBy(u => u.Id, StringComparer.Ordinal)
        }))));

    private static async Task<FulfillmentOrder> ReadServiceOrderAsync(MySqlConnection connection,
        MySqlTransaction? tx, string orderId, bool locked, CancellationToken ct)
        => await connection.QuerySingleOrDefaultAsync<FulfillmentOrder>(new CommandDefinition("""
            SELECT ID AS Id, SESSION_ID AS SessionId, FLOW_VERSION AS FlowVersion,
                CAST(BUSINESS_PERIOD_ID AS CHAR(36)) AS BusinessPeriodId, ORDER_STATUS AS OrderStatus,
                STORE_CONFIRMATION_STATUS AS StoreConfirmationStatus, MEAL_CREDIT_APPLIED AS MealCreditApplied,
                CAST(UPDATED_AT AS CHAR) AS Revision, STARTED_AT AS ActualStartsAt
            FROM ORDERS WHERE ID=@OrderId
            """ + (locked ? " FOR UPDATE;" : ";"), new { OrderId = orderId }, tx, cancellationToken: ct))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");

    private static async Task<FulfillmentUnit[]> ReadServiceUnitsAsync(MySqlConnection connection,
        MySqlTransaction? tx, FulfillmentOrder order, bool locked, CancellationToken ct)
    {
        if (order.FlowVersion >= 2)
            return (await connection.QueryAsync<FulfillmentUnit>(new CommandDefinition(UnitSelect +
                " WHERE ORDER_ID=@OrderId AND KIND='nominee' ORDER BY ID" + (locked ? " FOR UPDATE;" : ";"),
                new { OrderId = order.Id }, tx, cancellationToken: ct))).ToArray();
        // V1 remains a projection over its original records; no historical FLOW_VERSION conversion.
        var units = (await connection.QueryAsync<FulfillmentUnit>(new CommandDefinition("""
            SELECT ID AS Id, ID AS NomineeId, STAFF_ID AS StaffId, 'nominee' AS Kind,
                CONCAT(STAFF_NAME_SNAPSHOT,'｜',SERVICE_NAME_SNAPSHOT) AS Name,
                1 AS Quantity, 1 AS Version, CONFIRMATION_STATUS AS Status,
                RESERVED_MINUTES AS PurchasedMinutes, BUFFER_MINUTES_SNAPSHOT AS RestMinutesReserved,
                REQUESTED_STARTS_AT AS ScheduledStartsAt, REQUESTED_SERVICE_ENDS_AT AS ScheduledEndsAt
            FROM ORDER_NOMINEES WHERE ORDER_ID=@OrderId ORDER BY ID
            """ + (locked ? " FOR UPDATE;" : ";"), new { OrderId = order.Id }, tx, cancellationToken: ct))).ToArray();
        foreach (var u in units)
        {
            u.CancelledQuantity = u.Status is "cancelled" or "rejected" ? 1 : 0;
            u.AcceptedQuantity = u.Status is "confirmed" or "completed" ? 1 : 0;
            u.StartedQuantity = order.OrderStatus is "in_service" or "completed" ? 1 : 0;
            u.CompletedQuantity = order.OrderStatus == "completed" || u.Status == "completed" ? 1 : 0;
            u.FulfillmentPeriodId = order.BusinessPeriodId;
            u.ActualStartsAt = order.ActualStartsAt;
        }
        return units;
    }

    public async Task<OrderServiceEligibilityDto> GetOrderServiceEligibilityAsync(string orderId, string role,
        string? staffId, DateTime now, CancellationToken ct)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        var order = await ReadServiceOrderAsync(connection, null, orderId, false, ct);
        var units = await ReadServiceUnitsAsync(connection, null, order, false, ct);
        var actions = new List<OrderServiceActionDto>();
        foreach (var action in new[] { "accept", "start_now", "backfill_scheduled", "backfill_custom", "complete" })
            actions.Add(await BuildServiceActionAsync(connection, null, order, units, action, role, staffId, now, ct));
        return new(orderId, order.FlowVersion, ServiceSnapshot(order, units), Offset(now)!.Value, actions);
    }

    private static async Task<OrderServiceActionDto> BuildServiceActionAsync(MySqlConnection connection,
        MySqlTransaction? tx, FulfillmentOrder order, FulfillmentUnit[] units, string action,
        string role, string? staffId, DateTime now, CancellationToken ct)
    {
        string? code = null, reason = null;
        void Block(string c, string r) { code ??= c; reason ??= r; }
        if (!IsOrderRepresentative(role, staffId, units)) Block("ORDER_SERVICE_SCOPE", "只有本單任一名有效指名人員或經理可以代辦。");
        if (order.StoreConfirmationStatus == "pending") Block("STORE_CONFIRMATION_REQUIRED", "請先等待店家承接協調單。");
        if (order.OrderStatus is "cancelled" or "rejected" or "completed") Block("ORDER_SERVICE_CLOSED", "此訂單已結案。");
        if (order.OrderStatus == "expired" && action != "backfill_custom") Block("ORDER_SERVICE_EXPIRED", "此失效歷史訂單請使用「改實際時間」，明確補登服務中或已完成。");
        if (order.FlowVersion < 2 && order.OrderStatus == "needs_reschedule") Block("NOMINEE_COORDINATION_REQUIRED", "此歷史訂單須先由經理重新安排。");
        var pending = units.Where(u => u.CancelledQuantity < u.Quantity && u.CompletedQuantity < u.Quantity)
            .Where(u => action == "complete" ? u.StartedQuantity > u.CompletedQuantity
                : action == "accept" ? u.AcceptedQuantity < u.Quantity : u.StartedQuantity == 0).ToArray();
        if (pending.Length == 0) Block("ORDER_SERVICE_NO_TARGETS", "沒有需要執行這項操作的指名服務。");
        if (units.Any(u => u.CancelledQuantity < u.Quantity && u.Status is "needs_coordination" or "carried_forward"))
            Block("NOMINEE_COORDINATION_REQUIRED", "本單有服務已交回協調或跨期，請先由經理安排。");
        if (action == "complete" && units.Any(u => u.StartedQuantity == 0 && u.CancelledQuantity < u.Quantity))
            Block("ORDER_SERVICE_NOT_STARTED", "本單尚有未開始的指名，請先開始或補登，不能直接完成整單服務。");
        if (action == "complete" && pending.Any(u => u.ScheduledEndsAt > now))
            Block("EARLY_COMPLETION_REASON_REQUIRED", "尚未到預計結束，提早完成請使用進階操作並填寫原因。");
        if (action == "complete" && pending.Length > 0 && await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM ORDER_SERVICE_ADDONS A JOIN ORDERS O ON O.ID=A.ORDER_ID
            WHERE A.PARENT_NOMINEE_ID IN @NomineeIds AND O.ORDER_STATUS NOT IN ('cancelled','rejected','expired','completed'));
            """, new { NomineeIds = pending.Select(u => u.NomineeId).ToArray() }, tx, cancellationToken: ct)))
            Block("FULFILLMENT_ADDONS_PENDING", "請先完成或取消本單指名尚未處理的加購，再完成原服務。");
        if (action is "accept" or "start_now" or "backfill_scheduled")
        {
            foreach (var periodId in pending.Select(u => u.FulfillmentPeriodId ?? order.BusinessPeriodId).Where(id => id is not null).Distinct())
                if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS(SELECT 1 FROM BUSINESS_PERIODS WHERE ID=@Id AND PERIOD_STATUS IN ('open','coordination'));",
                    new { Id = periodId }, tx, cancellationToken: ct)))
                    Block("FULFILLMENT_PERIOD_CLOSED", "履約營業期已關閉，請先跨期安排；歷史已完成服務請用「改實際時間」補登結束。");
        }
        var conflicts = new List<FulfillmentConflictDto>();
        var targets = new List<OrderServiceTargetDto>();
        foreach (var u in units)
        {
            var skip = !pending.Contains(u);
            var start = action == "start_now" ? now : u.ScheduledStartsAt;
            var end = start?.AddMinutes(u.PurchasedMinutes);
            targets.Add(new(u.Id, u.Name, u.StaffId ?? "", u.Version, u.Status, skip,
                u.PurchasedMinutes, u.RestMinutesReserved, Offset(start), Offset(end), Offset(end?.AddMinutes(u.RestMinutesReserved))));
            if (skip || action is "complete" or "backfill_custom" || code == "ORDER_SERVICE_SCOPE") continue;
            if (!start.HasValue || u.PurchasedMinutes <= 0) { Block("ORDER_SERVICE_TIME_REQUIRED", $"{u.Name} 缺少有效預約時間或購買時長。"); continue; }
            if (action == "accept" && start < now) Block("NOMINATION_START_IN_PAST", "預約開始時間已過，請選擇現在開始或按預約時間補登。");
            if (action == "backfill_scheduled" && start > now) Block("BACKFILL_START_IN_FUTURE", $"{u.Name} 的預約時間尚未到，請選擇現在開始或更正實際時間。");
            if (action == "backfill_scheduled" && end <= now) Block("BACKFILL_PROGRESS_REQUIRED", "已超過預計結束，請用「改實際時間」確認服務仍在進行或已完成。");
            var found = await FindServiceConflictsAsync(connection, tx, u, start.Value, end!.Value.AddMinutes(u.RestMinutesReserved), now, action != "accept", ct);
            conflicts.AddRange(found);
            if (found.Count > 0) Block("STAFF_TIME_CONFLICT", $"{u.Name} 的服務及休息時段與其他預約或進行中服務衝突。");
        }
        return new(action, code is null, code, reason, targets, conflicts);
    }

    private static async Task<IReadOnlyList<FulfillmentConflictDto>> FindServiceConflictsAsync(
        MySqlConnection connection, MySqlTransaction? tx, FulfillmentUnit unit,
        DateTime start, DateTime busyEnd, DateTime now, bool starting, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<FulfillmentConflictRow>(new CommandDefinition("""
            SELECT COALESCE(CAST(F.ID AS CHAR(36)),N.ID) AS UnitId, N.ORDER_ID AS OrderId,
                N.STAFF_NAME_SNAPSHOT AS Name, N.STAFF_ID AS StaffId,
                COALESCE(B.STARTS_AT,F.SCHEDULED_STARTS_AT,N.REQUESTED_STARTS_AT) AS StartsAt,
                COALESCE(B.ENDS_AT,DATE_ADD(F.SCHEDULED_ENDS_AT,INTERVAL F.REST_MINUTES_RESERVED MINUTE),N.REQUESTED_BUSY_UNTIL) AS EndsAt,
                COALESCE(F.UNIT_STATUS,O.ORDER_STATUS) AS Status, 0 AS OverlapMinutes
            FROM ORDER_NOMINEES N JOIN ORDERS O ON O.ID=N.ORDER_ID
            LEFT JOIN ORDER_FULFILLMENT_UNITS F ON F.NOMINEE_ID=N.ID
            LEFT JOIN STAFF_BUSY_BLOCKS B ON B.ORDER_NOMINEE_ID=N.ID AND B.BLOCK_STATUS='active'
            WHERE N.STAFF_ID=@StaffId AND N.ID<>@NomineeId
                AND ((N.CONFIRMATION_STATUS IN ('waiting','confirmed') AND O.ORDER_STATUS NOT IN ('cancelled','rejected','expired','completed')
                    AND COALESCE(F.UNIT_STATUS,'waiting') NOT IN ('cancelled','completed','needs_coordination','carried_forward'))
                    OR B.ENDS_AT>@Now)
                AND ((COALESCE(B.STARTS_AT,F.SCHEDULED_STARTS_AT,N.REQUESTED_STARTS_AT)<@EndsAt
                    AND COALESCE(B.ENDS_AT,DATE_ADD(F.SCHEDULED_ENDS_AT,INTERVAL F.REST_MINUTES_RESERVED MINUTE),N.REQUESTED_BUSY_UNTIL)>@StartsAt)
                    OR (@Starting=TRUE AND (F.STARTED_QUANTITY>F.COMPLETED_QUANTITY OR (O.FLOW_VERSION<2 AND O.ORDER_STATUS='in_service'))))
            ORDER BY StartsAt,N.ID;
            """, new { unit.StaffId, unit.NomineeId, StartsAt = start, EndsAt = busyEnd, Now = now, Starting = starting }, tx, cancellationToken: ct));
        return rows.Select(r => new FulfillmentConflictDto(r.UnitId, r.OrderId, r.Name, r.StaffId,
            Math.Max(0, (int)Math.Ceiling((new[] { busyEnd, r.EndsAt ?? busyEnd }.Min() - new[] { start, r.StartsAt ?? start }.Max()).TotalMinutes)),
            Offset(r.StartsAt), Offset(r.EndsAt), r.Status)).ToArray();
    }

    private static async Task ValidateServiceWindowAsync(MySqlConnection connection, MySqlTransaction tx,
        FulfillmentUnit unit, string action, bool completed, DateTime now, CancellationToken ct, bool stillInProgress = false)
    {
        if (!unit.ScheduledStartsAt.HasValue || !unit.ScheduledEndsAt.HasValue || unit.PurchasedMinutes <= 0)
            throw new BusinessException("服務缺少有效時間或購買時長。", "ORDER_SERVICE_TIME_REQUIRED");
        if (action == "backfill" && !completed && unit.ScheduledEndsAt <= now && !stillInProgress)
            throw new BusinessException("已超過預計結束，請明確選擇服務仍在進行或填寫實際結束時間。", "BACKFILL_PROGRESS_REQUIRED");
        if (!completed && unit.FulfillmentPeriodId is not null)
        {
            var open = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM BUSINESS_PERIODS WHERE ID=@Id AND PERIOD_STATUS IN ('open','coordination'));",
                new { Id = unit.FulfillmentPeriodId }, tx, cancellationToken: ct));
            if (!open) throw new BusinessException("此服務的履約營業期已關閉，請先由經理跨期安排；歷史已完成請填寫結束時間。", "FULFILLMENT_PERIOD_CLOSED");
        }
        var end = completed ? unit.ActualEndsAt!.Value : unit.ScheduledEndsAt.Value;
        var conflicts = await FindServiceConflictsAsync(connection, tx, unit, unit.ScheduledStartsAt.Value,
            end.AddMinutes(unit.RestMinutesReserved), now, !completed && action != "accept", ct);
        if (conflicts.Count > 0)
            throw new ConflictException($"{unit.Name} 的服務及休息時段與 {string.Join("、", conflicts.Select(c => $"{c.Name}（{c.StartsAt:MM/dd HH:mm}–{c.EndsAt:HH:mm}）"))} 衝突，請重新核對時段。", "STAFF_TIME_CONFLICT");
    }

    public async Task<OrderServiceCommandResultDto> ExecuteOrderServiceAsync(string orderId,
        OrderServiceCommandRequest request, string actorId, string role, string? staffId, DateTime now, CancellationToken ct)
    {
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CompatibilityPayload ?? JsonSerializer.Serialize(request))));
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        var sessionId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT SESSION_ID FROM ORDERS WHERE ID=@OrderId;", new { OrderId = orderId }, tx, cancellationToken: ct))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ID FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId FOR UPDATE;", new { SessionId = sessionId }, tx, cancellationToken: ct));
        var order = await ReadServiceOrderAsync(connection, tx, orderId, true, ct);
        await RequireOrderRepresentativeAsync(connection, tx, orderId, role, staffId, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<FulfillmentOperation>(new CommandDefinition("""
            SELECT CAST(UNIT_ID AS CHAR(36)) AS UnitId,PAYLOAD_HASH AS PayloadHash,RESULT_JSON AS ResultJson FROM ORDER_FULFILLMENT_OPERATIONS
            WHERE ORDER_ID=@OrderId AND OPERATION_ID=@OperationId;
            """, new { OrderId = orderId, request.OperationId }, tx, cancellationToken: ct));
        if (previous is not null)
        {
            if (previous.UnitId == request.ExpectedUnitId && previous.UnitId is not null && previous.PayloadHash == request.LegacyPayloadHash)
            {
                var legacy = JsonSerializer.Deserialize<OrderFulfillmentDto>(previous.ResultJson)!;
                await tx.CommitAsync(ct);
                return new(orderId, request.OperationId, legacy.OrderStatus, [previous.UnitId]);
            }
            if (previous.PayloadHash != payloadHash) throw new ConflictException("操作識別碼已用於不同內容。", "OPERATION_PAYLOAD_CONFLICT");
            await tx.CommitAsync(ct);
            return JsonSerializer.Deserialize<OrderServiceCommandResultDto>(previous.ResultJson)!;
        }
        var units = await ReadServiceUnitsAsync(connection, tx, order, true, ct);
        await LockStaffRowsAsync(connection, tx, units.Where(u => u.StaffId is not null).Select(u => u.StaffId!), ct);
        if (request.ExpectedUnitId is not null && !units.Any(u => u.Id == request.ExpectedUnitId && u.Version == request.ExpectedUnitVersion))
            throw new ConflictException("服務已變更，請重新整理。", "FULFILLMENT_VERSION_CONFLICT");
        if (request.Snapshot != ServiceSnapshot(order, units)) throw new ConflictException("同單服務已變更，請重新核對後操作。", "FULFILLMENT_VERSION_CONFLICT");
        var eligibility = await BuildServiceActionAsync(connection, tx, order, units, request.Action, role, staffId, now, ct);
        if (!eligibility.Enabled && !(eligibility.ReasonCode == "EARLY_COMPLETION_REASON_REQUIRED" && request.Action == "complete" && !string.IsNullOrWhiteSpace(request.Reason)))
            throw new ConflictException(eligibility.Reason!, eligibility.ReasonCode!);
        var targets = units.Where(u => eligibility.Targets.Any(t => t.Id == u.Id && !t.Skipped)).ToArray();
        if (request.Action == "backfill_custom" && (request.ActualTimes.Count != targets.Length ||
            request.ActualTimes.Select(t => t.Id).Distinct().Count() != targets.Length ||
            targets.Any(u => !request.ActualTimes.Any(t => t.Id == u.Id))))
            throw new BusinessException("請填寫每位待補登指名的實際開始及進度。", "BACKFILL_TARGETS_REQUIRED");
        // Preflight all target windows; all state transitions and remaining guards share this transaction.
        var commands = new List<(FulfillmentUnit Unit, FulfillmentTransitionRequest Request)>();
        foreach (var u in targets)
        {
            var actual = request.ActualTimes.SingleOrDefault(t => t.Id == u.Id);
            var command = new FulfillmentTransitionRequest {
                OperationId = request.OperationId, ExpectedVersion = u.Version, Quantity = u.Quantity - u.CancelledQuantity,
                Action = request.Action.StartsWith("backfill_") ? "backfill" : request.Action,
                Reason = request.Reason ?? (request.Action == "backfill_scheduled" ? "代表確認各指名已按有效預約時間開始，補登漏按開始。" : null),
                ActualStartsAt = request.Action == "backfill_scheduled" ? Offset(u.ScheduledStartsAt) : actual?.StartsAt,
                ActualEndsAt = actual?.EndsAt, StillInProgress = actual?.StillInProgress ?? false
            };
            if (command.Action == "backfill")
            {
                if (!command.ActualStartsAt.HasValue || command.ActualStartsAt.Value == default || command.ActualStartsAt > Offset(now) ||
                    command.ActualEndsAt > Offset(now) || command.ActualEndsAt < command.ActualStartsAt ||
                    actual is { StillInProgress: true, EndsAt: not null } || actual is { StillInProgress: false, EndsAt: null })
                    throw new BusinessException("請確認實際開始、結束及服務進度；時間不可在未來。", "FULFILLMENT_BACKFILL_TIME_INVALID");
            }
            // Validate a copy; original snapshots remain untouched until the command core saves them.
            if (command.Action is "backfill" or "start_now")
            {
                var projected = JsonSerializer.Deserialize<FulfillmentUnit>(JsonSerializer.Serialize(u))!;
                projected.ScheduledStartsAt = command.Action == "start_now" ? now : command.ActualStartsAt!.Value.ToOffset(TimeSpan.FromHours(8)).DateTime;
                projected.ScheduledEndsAt = projected.ScheduledStartsAt.Value.AddMinutes(u.PurchasedMinutes);
                projected.ActualEndsAt = command.ActualEndsAt?.ToOffset(TimeSpan.FromHours(8)).DateTime;
                await ValidateServiceWindowAsync(connection, tx, projected, command.Action, command.ActualEndsAt.HasValue, now, ct, command.StillInProgress);
            }
            commands.Add((u, command));
        }
        if (order.FlowVersion < 2 && order.OrderStatus == "expired")
            await RestoreExpiredServiceCreditAsync(connection, tx, order, now, ct);
        foreach (var (unit, command) in commands)
        {
            if (order.FlowVersion >= 2)
                await ApplyFulfillmentTransitionAsync(connection, tx, order, unit, command, actorId, role, now, ct);
            else
                await ApplyLegacyServiceCommandAsync(connection, tx, order, unit, command, actorId, role, now, ct);
        }
        if (order.FlowVersion < 2)
        {
            var next = request.Action == "accept" ? "confirmed" : request.Action == "complete" ||
                commands.All(c => c.Request.ActualEndsAt.HasValue) ? "completed" : "in_service";
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ORDERS SET ORDER_STATUS=@Next,UPDATED_AT=@Now,UPDATED_BY=@ActorId,
                    CONFIRMED_AT=COALESCE(CONFIRMED_AT,@Now),QUEUE_ENTERED_AT=NULL,
                    STARTED_AT=CASE WHEN @Next IN ('in_service','completed') THEN @StartedAt ELSE STARTED_AT END,
                    COMPLETED_AT=CASE WHEN @Next='completed' THEN @EndedAt ELSE NULL END
                WHERE ID=@OrderId;
                """, new { OrderId = orderId, Next = next, Now = now, ActorId = actorId,
                    StartedAt = commands.Min(c => c.Unit.ActualStartsAt), EndedAt = commands.Max(c => c.Unit.ActualEndsAt) }, tx, cancellationToken: ct));
            await InsertHistoryAsync(connection, tx, orderId, order.OrderStatus, next,
                $"整單代表操作 {request.Action}，共 {targets.Length} 位。{request.Reason}", "staff", actorId, now, ct);
        }
        var status = await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ORDER_STATUS FROM ORDERS WHERE ID=@OrderId;", new { OrderId = orderId }, tx, cancellationToken: ct));
        var result = new OrderServiceCommandResultDto(orderId, request.OperationId, status ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND"), targets.Select(t => t.Id).ToArray());
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDER_FULFILLMENT_OPERATIONS (ORDER_ID,OPERATION_ID,UNIT_ID,OPERATION_SCOPE,PAYLOAD_HASH,RESULT_JSON,ACTOR_ID,CREATED_AT)
            VALUES (@OrderId,@OperationId,NULL,'order',@PayloadHash,@Result,@ActorId,@Now);
            """, new { OrderId = orderId, request.OperationId, PayloadHash = payloadHash, Result = JsonSerializer.Serialize(result), ActorId = actorId, Now = now }, tx, cancellationToken: ct));
        await LinkOperationUnitsAsync(connection, tx, orderId, request.OperationId, result.AffectedIds, ct, order.FlowVersion < 2 ? "nominee" : "unit");
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task RestoreExpiredServiceCreditAsync(MySqlConnection connection, MySqlTransaction tx,
        FulfillmentOrder order, DateTime now, CancellationToken ct)
    {
        if (order.MealCreditApplied <= 0) return;
        var rows = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE CUSTOMER_ORDER_SESSIONS SET REMAINING_MEAL_CREDIT=REMAINING_MEAL_CREDIT-@Credit,UPDATED_AT=@Now
            WHERE ID=@SessionId AND REMAINING_MEAL_CREDIT>=@Credit;
            """, new { order.SessionId, Credit = order.MealCreditApplied, Now = now }, tx, cancellationToken: ct));
        if (rows != 1) throw new BusinessException("信物餘額不足以補回失效訂單折抵，請先由後台處理。", "MEAL_CREDIT_UNAVAILABLE_FOR_BACKFILL");
    }

    private static async Task ApplyLegacyServiceCommandAsync(MySqlConnection connection, MySqlTransaction tx,
        FulfillmentOrder order, FulfillmentUnit unit, FulfillmentTransitionRequest request,
        string actorId, string role, DateTime now, CancellationToken ct)
    {
        var before = JsonSerializer.Serialize(unit);
        unit.OriginalScheduledStartsAt ??= unit.ScheduledStartsAt;
        unit.OriginalScheduledEndsAt ??= unit.ScheduledEndsAt;
        unit.ActualStartsAt = request.Action == "start_now" ? now : request.ActualStartsAt?.ToOffset(TimeSpan.FromHours(8)).DateTime ?? unit.ActualStartsAt;
        unit.ActualEndsAt = request.Action == "complete" ? now : request.ActualEndsAt?.ToOffset(TimeSpan.FromHours(8)).DateTime;
        if (unit.ActualStartsAt.HasValue)
        {
            unit.ScheduledStartsAt = unit.ActualStartsAt;
            unit.ScheduledEndsAt = unit.ActualStartsAt.Value.AddMinutes(unit.PurchasedMinutes);
        }
        unit.CompletedQuantity = unit.ActualEndsAt.HasValue ? 1 : 0;
        await ApplyNomineeFulfillmentAsync(connection, tx, order, unit, request.Action, actorId, now, ct);
        await InsertHistoryAsync(connection, tx, order.Id, order.OrderStatus, order.OrderStatus,
            $"整單代表操作 {request.Action}：{unit.Name}。{request.Reason}", "staff", actorId, now, ct);
        await InsertAuditAsync(connection, tx, order.Id, order.SessionId, $"order_service.{request.Action}",
            before, JsonSerializer.Serialize(new { unit, request.OperationId }), actorId, role, now, ct);
    }

    private static Task LinkOperationUnitsAsync(MySqlConnection connection, MySqlTransaction tx,
        string orderId, string operationId, IEnumerable<string> ids, CancellationToken ct, string targetKind = "unit")
        => connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDER_FULFILLMENT_OPERATION_UNITS (ORDER_ID,OPERATION_ID,UNIT_ID,TARGET_KIND)
            VALUES (@OrderId,@OperationId,@UnitId,@TargetKind);
            """, ids.Select(id => new { OrderId = orderId, OperationId = operationId, UnitId = id, TargetKind = targetKind }), tx, cancellationToken: ct));
}
