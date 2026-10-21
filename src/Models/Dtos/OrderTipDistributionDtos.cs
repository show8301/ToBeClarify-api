using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record OrderTipDistributionSnapshot(string Mode, IReadOnlyList<OrderingFinanceTipAllocationDto> Allocations, int PublicAmount = 0);
public sealed record OrderTipDistributionDto(string OrderId, string OrderNumber, string ItemId, int Amount,
    int Version, OrderTipDistributionSnapshot? Distribution, string? StaffName, int StaffAmount, int StoreAmount);

public sealed record CorrectOrderTipDistributionRequest
{
    [Required] public string OperationId { get; init; } = "";
    public long ExpectedAccountVersion { get; init; }
    public int ExpectedDistributionVersion { get; init; }
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; init; } = "";
    [Required] public string Mode { get; init; } = "";
    [Required, MaxLength(200)] public IReadOnlyList<SaveOrderingFinanceTipAllocationRequest> Allocations { get; init; } = [];
}
