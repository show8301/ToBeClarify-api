using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public sealed partial class OrderingService
{
    public Task<OrderServiceEligibilityDto> GetOrderServiceEligibilityAsync(string orderId, ClaimsPrincipal actor, CancellationToken ct)
        => _repository.GetOrderServiceEligibilityAsync(orderId, ActorRole(actor),
            actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType), _clock.LocalDateTime, ct);

    public Task<OrderServiceCommandResultDto> ExecuteOrderServiceAsync(string orderId, OrderServiceCommandRequest request,
        ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!Guid.TryParse(request.OperationId, out _) || request.Snapshot.Length != 64 ||
            request.Action is not ("accept" or "start_now" or "backfill_scheduled" or "backfill_custom" or "complete"))
            throw new BusinessException("請確認操作識別碼、版本及動作。", "ORDER_SERVICE_REQUEST_INVALID");
        request.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (request.Reason?.Length > 500 || request.ActualTimes is null || request.ActualTimes.Count > 100 || request.ActualTimes.Any(t => t is null))
            throw new BusinessException("操作內容或原因長度不正確。", "ORDER_SERVICE_REQUEST_INVALID");
        if (request.Action == "backfill_custom" && request.Reason is null)
            throw new BusinessException("更正實際時間請填寫原因。", "FULFILLMENT_BACKFILL_REASON_REQUIRED");
        if (request.Action != "backfill_custom" && request.ActualTimes.Count != 0)
            throw new BusinessException("此快捷動作不接受自訂時間。", "ORDER_SERVICE_REQUEST_INVALID");
        return _repository.ExecuteOrderServiceAsync(orderId, request, ActorId(actor), ActorRole(actor),
            actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType), _clock.LocalDateTime, ct);
    }

    private async Task RequireServiceRepresentativeAsync(string orderId, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!await _repository.CanRepresentOrderAsync(orderId, ActorRole(actor), actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType), ct))
            throw new ForbiddenException("只有本單任一名有效指名人員或經理可以代辦。", "ORDER_SERVICE_SCOPE");
    }

    public async Task<IReadOnlyList<OrderDto>> GetServiceOrdersAsync(DateOnly day, CancellationToken ct)
        => await MapOrdersAsync(await _repository.GetServiceOrdersAsync(day, ct), ct, includeFulfillment: false);

    public async Task<IReadOnlyList<AdminOrderSessionDto>> GetServiceSessionsAsync(DateOnly day, CancellationToken ct)
        => (await _repository.GetServiceSessionsAsync(day, ct)).Select(row => new AdminOrderSessionDto(
            MapSession(row), row.OrderCount, row.WaitingOrderCount, row.ConfirmedOrderCount, row.TotalAmount, ToOffset(row.LastOrderedAt))).ToArray();

    public async Task<IReadOnlyList<OrderFulfillmentDto>> GetServiceFulfillmentAsync(DateOnly day, ClaimsPrincipal actor, CancellationToken ct)
        => await Task.WhenAll((await _repository.GetServiceFulfillmentAsync(day, ct)).Select(data => ScopeFulfillmentAsync(data, actor, ct)));

    public async Task<IReadOnlyList<AdminOrderSessionDto>> GetDesignatedSessionsAsync(DateOnly? businessDate,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        if (string.IsNullOrWhiteSpace(staffId))
            throw new ForbiddenException("此帳號尚未綁定店員。", "DESIGNATED_STAFF_REQUIRED");
        var day = businessDate ?? (await GetBusinessContextAsync(cancellationToken)).ReferenceBusinessDate;
        return (await _repository.GetDesignatedSessionsAsync(day, staffId, cancellationToken)).Select(row =>
            new AdminOrderSessionDto(MapSession(row), row.OrderCount, row.WaitingOrderCount,
                row.ConfirmedOrderCount, row.TotalAmount, ToOffset(row.LastOrderedAt))).ToArray();
    }

    public async Task<OrderDto> RespondNomineeAsync(string orderId, string nomineeId,
        NomineeResponseRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        var bundle = await _repository.GetOrderAsync(orderId, cancellationToken);
        var order = bundle.Orders.SingleOrDefault(o => o.Id == orderId)
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        var nominee = bundle.Nominees.SingleOrDefault(n => n.OrderId == orderId && n.Id == nomineeId);
        if (nominee is null) throw new BusinessException("找不到指名。", "ORDER_NOMINEE_NOT_FOUND");
        await RequireServiceRepresentativeAsync(orderId, actor, cancellationToken);
        if (!Guid.TryParse(request.OperationId, out _) || request.Decision is not ("accept" or "decline"))
            throw new BusinessException("請確認回應操作。", "NOMINEE_RESPONSE_INVALID");
        if (order.StoreConfirmationStatus == "pending")
            throw new BusinessException("請先等待店家承接協調單。", "STORE_CONFIRMATION_REQUIRED");
        var reason = request.Reason?.Trim();
        if (reason?.Length > 500 || request.Decision == "decline" && string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("無法承接時請填寫原因，最多 500 字。", "NOMINEE_RESPONSE_REASON_REQUIRED");
        if (order.FlowVersion >= 2)
        {
            var fulfillment = await _repository.GetFulfillmentAsync(orderId, cancellationToken);
            var unit = fulfillment.Units.Single(u => u.Kind == "nominee" && u.NomineeId == nomineeId);
            if (request.Decision == "accept")
            {
                var eligibility = await GetOrderServiceEligibilityAsync(orderId, actor, cancellationToken);
                await ExecuteOrderServiceAsync(orderId, new OrderServiceCommandRequest {
                    OperationId = request.OperationId, Snapshot = eligibility.Snapshot, Action = "accept",
                    CompatibilityPayload = $"nominee-response:{nomineeId}:" + System.Text.Json.JsonSerializer.Serialize(request),
                    ExpectedUnitId = unit.Id, ExpectedUnitVersion = request.ExpectedVersion,
                    LegacyPayloadHash = ToBeClarify.Api.Repositories.Ordering.FulfillmentOperationFingerprint.Create(unit.Id,
                        new FulfillmentTransitionRequest { OperationId = request.OperationId, ExpectedVersion = request.ExpectedVersion,
                            Action = "accept", Reason = reason })
                }, actor, cancellationToken);
            }
            else await TransitionFulfillmentAsync(orderId, unit.Id, new FulfillmentTransitionRequest {
                OperationId = request.OperationId, ExpectedVersion = request.ExpectedVersion,
                Action = request.Decision, Reason = reason
            }, actor, cancellationToken);
        }
        else if (request.Decision == "accept")
            await _repository.ConfirmNomineeAsync(orderId, staffId, ActorId(actor), ActorRole(actor) is AdminRole.Manager or AdminRole.Developer, _clock.LocalDateTime, cancellationToken);
        else
            await _repository.DeclineLegacyNomineeAsync(orderId, nomineeId, staffId ?? "", ActorId(actor), ActorRole(actor), reason!, _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<OrderFulfillmentDto> GetFulfillmentAsync(string orderId, ClaimsPrincipal actor, CancellationToken cancellationToken)
        => await ScopeFulfillmentAsync(await _repository.GetFulfillmentAsync(orderId, cancellationToken), actor, cancellationToken);

    public async Task<FulfillmentStartPreviewDto> GetFulfillmentStartPreviewAsync(string orderId, string unitId,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var preview = await _repository.GetFulfillmentStartPreviewAsync(orderId, unitId,
            _clock.LocalDateTime, cancellationToken);
        await RequireServiceRepresentativeAsync(orderId, actor, cancellationToken);
        var eligibility = await GetOrderServiceEligibilityAsync(orderId, actor, cancellationToken);
        return preview with { CanStartNow = preview.CanStartNow && eligibility.Actions.Single(a => a.Action == "start_now").Enabled };
    }

    public Task<IReadOnlyList<FulfillmentPeriodOptionDto>> GetFulfillmentPeriodOptionsAsync(string orderId,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
        => _repository.GetFulfillmentPeriodOptionsAsync(orderId, cancellationToken);

    public async Task<OrderFulfillmentDto> TransitionFulfillmentAsync(string orderId, string unitId,
        FulfillmentTransitionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.OperationId, out _) || request.ExpectedVersion < 1 || request.Quantity < 1)
            throw new BusinessException("操作識別碼、資料版本或數量不正確。", "FULFILLMENT_REQUEST_INVALID");
        if (request.Action is not ("accept" or "decline" or "start" or "start_now" or "backfill" or "complete" or "cancel" or "reschedule" or "carry_forward"))
            throw new BusinessException("不支援此分項操作。", "FULFILLMENT_ACTION_INVALID");
        request.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (request.Reason?.Length > 500)
            throw new BusinessException("原因最多 500 字。", "FULFILLMENT_REASON_TOO_LONG");
        if (request.Action is "cancel" or "decline" or "reschedule" or "carry_forward" && request.Reason is null)
            throw new BusinessException("請填寫此次取消或改期的原因。", "FULFILLMENT_REASON_REQUIRED");
        request.CompensationReason = string.IsNullOrWhiteSpace(request.CompensationReason) ? null : request.CompensationReason.Trim();
        if (request.CompensationAmount < 0 || request.CompensationAmount > 2_000_000_000)
            throw new BusinessException("折讓金額不正確。", "FULFILLMENT_COMPENSATION_INVALID");
        if (request.CompensationAmount > 0 && request.CompensationReason is null)
            throw new BusinessException("有折讓時請填寫原因。", "FULFILLMENT_COMPENSATION_REASON_REQUIRED");
        if (request.RestMinutes < -1 || request.RestMinutes > 1440)
            throw new BusinessException("休息分鐘數不正確。", "FULFILLMENT_REST_INVALID");
        if (request.Action == "backfill" && request.Reason is null)
            throw new BusinessException("補登服務請填寫原因。", "FULFILLMENT_BACKFILL_REASON_REQUIRED");
        return await ScopeFulfillmentAsync(await _repository.TransitionFulfillmentAsync(orderId, unitId, request,
            ActorId(actor), ActorRole(actor), actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType),
            _clock.LocalDateTime, cancellationToken), actor, cancellationToken);
    }

    private async Task<OrderFulfillmentDto> ScopeFulfillmentAsync(OrderFulfillmentDto data, ClaimsPrincipal actor, CancellationToken ct)
        => ScopeFulfillment(data, actor, await _repository.CanRepresentOrderAsync(data.OrderId, ActorRole(actor),
            actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType), ct));

    private static OrderFulfillmentDto ScopeFulfillment(OrderFulfillmentDto data, ClaimsPrincipal actor, bool representative)
    {
        var privileged = ActorRole(actor) is AdminRole.Manager or AdminRole.Developer;
        return data with { Units = data.Units.Select(unit =>
        {
            var actions = new List<string>();
            if (data.FlowVersion >= 2 && (unit.StaffId is null || privileged || representative))
            {
                if (unit.Status == "needs_coordination")
                {
                    if (privileged) actions.AddRange(["reschedule", "carry_forward", "cancel"]);
                    return unit with { AllowedActions = actions };
                }
                if (unit.Kind == "nominee" && representative && unit.Status == "waiting" && unit.AcceptedQuantity == 0 && unit.StartedQuantity == 0)
                    actions.Add("decline");
                if (unit.AcceptedQuantity + unit.CancelledQuantity < unit.Quantity) actions.Add("accept");
                if (unit.Kind != "nominee" && unit.StartedQuantity < unit.AcceptedQuantity) actions.Add("start");
                if (unit.Kind == "nominee" && unit.StartedQuantity == 0 && unit.CancelledQuantity == 0)
                {
                    actions.Add("start_now");
                }
                if ((unit.Kind is "nominee" or "addon" && unit.StartedQuantity == 0 && unit.CancelledQuantity == 0) ||
                    (unit.Kind is "meal" or "room" && unit.CompletedQuantity < unit.Quantity && unit.CancelledQuantity == 0))
                    actions.Add("backfill");
                if (unit.CompletedQuantity < unit.StartedQuantity) actions.Add("complete");
                if (unit.StartedQuantity + unit.CancelledQuantity < unit.Quantity) actions.Add("cancel");
                if (unit.Kind == "nominee" && unit.StartedQuantity == 0 && unit.CancelledQuantity == 0) actions.Add("reschedule");
                if (unit.Kind is not "addon" && unit.StartedQuantity == 0 && unit.CancelledQuantity < unit.Quantity)
                    actions.Add("carry_forward");
            }
            return unit with { AllowedActions = actions };
        }).ToArray() };
    }
}
