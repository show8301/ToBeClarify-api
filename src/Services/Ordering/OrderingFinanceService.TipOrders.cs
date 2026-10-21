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
        if (!Guid.TryParse(request.OperationId, out var op) || request.ExpectedAccountVersion < 0 ||
            request.ExpectedDistributionVersion < 0 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new BusinessException("請填寫更正原因及有效版本。", "TIP_CORRECTION_INVALID");
        var normalized = request with { OperationId = op.ToString(), Reason = request.Reason.Trim() };
        return repository.CorrectOrderTipAsync(sessionId, itemId, normalized,
            Hash(new { sessionId, itemId, request = normalized }), ActorId(actor), ct);
    }
}
