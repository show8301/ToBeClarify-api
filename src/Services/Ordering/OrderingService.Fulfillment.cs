using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public sealed partial class OrderingService
{
    public async Task<OrderFulfillmentDto> GetFulfillmentAsync(string orderId, ClaimsPrincipal actor, CancellationToken cancellationToken)
        => ScopeFulfillment(await _repository.GetFulfillmentAsync(orderId, cancellationToken), actor);

    public async Task<FulfillmentStartPreviewDto> GetFulfillmentStartPreviewAsync(string orderId, string unitId,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var preview = await _repository.GetFulfillmentStartPreviewAsync(orderId, unitId,
            _clock.LocalDateTime, cancellationToken);
        var privileged = ActorRole(actor) is AdminRole.Manager or AdminRole.Developer;
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        if (!privileged && preview.Conflicts.Any(c => c.StaffId is not null && c.StaffId != staffId))
            return preview with { Conflicts = Array.Empty<FulfillmentConflictDto>() };
        return preview;
    }

    public Task<IReadOnlyList<FulfillmentPeriodOptionDto>> GetFulfillmentPeriodOptionsAsync(string orderId,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
        => _repository.GetFulfillmentPeriodOptionsAsync(orderId, cancellationToken);

    public async Task<OrderFulfillmentDto> TransitionFulfillmentAsync(string orderId, string unitId,
        FulfillmentTransitionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.OperationId, out _) || request.ExpectedVersion < 1 || request.Quantity < 1)
            throw new BusinessException("操作識別碼、資料版本或數量不正確。", "FULFILLMENT_REQUEST_INVALID");
        if (request.Action is not ("accept" or "start" or "start_now" or "backfill" or "complete" or "cancel" or "reschedule"))
            throw new BusinessException("不支援此分項操作。", "FULFILLMENT_ACTION_INVALID");
        request.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (request.Reason?.Length > 500)
            throw new BusinessException("原因最多 500 字。", "FULFILLMENT_REASON_TOO_LONG");
        if (request.Action is "cancel" or "reschedule" && request.Reason is null)
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
        return ScopeFulfillment(await _repository.TransitionFulfillmentAsync(orderId, unitId, request,
            ActorId(actor), ActorRole(actor), actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType),
            _clock.LocalDateTime, cancellationToken), actor);
    }

    private static OrderFulfillmentDto ScopeFulfillment(OrderFulfillmentDto data, ClaimsPrincipal actor)
    {
        var privileged = ActorRole(actor) is AdminRole.Manager or AdminRole.Developer;
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        return data with { Units = data.Units.Select(unit =>
        {
            var actions = new List<string>();
            if (data.FlowVersion >= 2 && (unit.StaffId is null || privileged || unit.StaffId == staffId))
            {
                if (unit.AcceptedQuantity + unit.CancelledQuantity < unit.Quantity) actions.Add("accept");
                if (unit.StartedQuantity < unit.AcceptedQuantity) actions.Add("start");
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
            }
            return unit with { AllowedActions = actions };
        }).ToArray() };
    }
}
