using System.Security.Claims;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public sealed partial class OrderingFinanceService
{
    public Task<IReadOnlyList<OrderTipDistributionDto>> GetOrderTipDistributionsAsync(string sessionId, CancellationToken ct)
        => repository.GetOrderTipDistributionsAsync(sessionId, ct);

    public Task<OrderingFinanceOperationDto> CorrectOrderTipAsync(string sessionId, string itemId,
        CorrectOrderTipDistributionRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        return SaveOrderTipAsync(sessionId, itemId, request, actor, ct, false);
    }

    public Task<OrderingFinanceOperationDto> VoidOrderTipAsync(string sessionId, string itemId,
        VoidOrderTipRequest request, ClaimsPrincipal actor, CancellationToken ct)
        => SaveOrderTipAsync(sessionId, itemId, new CorrectOrderTipDistributionRequest {
            OperationId = request.OperationId, ExpectedAccountVersion = request.ExpectedAccountVersion,
            ExpectedDistributionVersion = request.ExpectedDistributionVersion, Reason = request.Reason
        }, actor, ct, true);

    private Task<OrderingFinanceOperationDto> SaveOrderTipAsync(string sessionId, string itemId,
        CorrectOrderTipDistributionRequest request, ClaimsPrincipal actor, CancellationToken ct, bool voidTip)
    {
        if (!Guid.TryParse(request.OperationId, out var op) || request.ExpectedAccountVersion < 0 ||
            request.ExpectedDistributionVersion < 0 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new BusinessException("請填寫更正原因及有效版本。", "TIP_CORRECTION_INVALID");
        var normalized = request with { OperationId = op.ToString(), Reason = request.Reason.Trim() };
        return repository.CorrectOrderTipAsync(sessionId, itemId, normalized,
            voidTip ? Hash(new { action = "void", sessionId, itemId, request = normalized })
                    : Hash(new { sessionId, itemId, request = normalized }), ActorId(actor), ct, voidTip);
    }
}
