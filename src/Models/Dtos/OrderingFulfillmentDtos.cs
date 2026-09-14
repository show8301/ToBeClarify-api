namespace ToBeClarify.Api.Models.Dtos;

public sealed record FulfillmentUnitDto(
    string Id, string? OrderItemId, string? NomineeId, string Kind, string Name,
    string? StaffId, int Quantity, int AcceptedQuantity, int StartedQuantity,
    int CompletedQuantity, int CancelledQuantity, string Status, int Version,
    int OriginalAmount, int OriginalCredit, int CancelledAmount, int ReturnedCredit,
    int PurchasedMinutes, DateTimeOffset? ScheduledStartsAt, DateTimeOffset? ScheduledEndsAt,
    DateTimeOffset? ActualStartsAt, DateTimeOffset? ActualEndsAt, IReadOnlyList<string> AllowedActions);

public sealed record OrderFulfillmentDto(string OrderId, string? BusinessPeriodId, int FlowVersion,
    string OrderStatus, IReadOnlyList<FulfillmentUnitDto> Units);

public sealed class FulfillmentTransitionRequest
{
    public string OperationId { get; set; } = string.Empty;
    public int ExpectedVersion { get; set; }
    public string Action { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string? Reason { get; set; }
    public DateTimeOffset? ScheduledStartsAt { get; set; }
}
