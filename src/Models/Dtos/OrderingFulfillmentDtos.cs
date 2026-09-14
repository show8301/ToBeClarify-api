namespace ToBeClarify.Api.Models.Dtos;

public sealed record FulfillmentUnitDto(
    string Id, string? OrderItemId, string? NomineeId, string Kind, string Name,
    string? StaffId, int Quantity, int AcceptedQuantity, int StartedQuantity,
    int CompletedQuantity, int CancelledQuantity, string Status, int Version,
    int OriginalAmount, int OriginalCredit, int CancelledAmount, int ReturnedCredit,
    int PurchasedMinutes, DateTimeOffset? ScheduledStartsAt, DateTimeOffset? ScheduledEndsAt,
    DateTimeOffset? ActualStartsAt, DateTimeOffset? ActualEndsAt,
    DateTimeOffset? OriginalScheduledStartsAt, DateTimeOffset? OriginalScheduledEndsAt,
    int RestMinutesReserved, string? FulfillmentPeriodId, IReadOnlyList<string> AllowedActions);

public sealed record FulfillmentConflictDto(
    string UnitId, string OrderId, string Name, string? StaffId, int OverlapMinutes,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string Status);

public sealed record FulfillmentStartPreviewDto(
    string OrderId, string UnitId, DateTimeOffset OriginalStartsAt, DateTimeOffset EffectiveStartsAt,
    DateTimeOffset EffectiveEndsAt, int PurchasedMinutes, int OriginalRestMinutes,
    int RestMinutesReserved, IReadOnlyList<FulfillmentConflictDto> Conflicts, bool CanStartNow);

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
    public string? TargetBusinessPeriodId { get; set; }
    public int RestMinutes { get; set; } = -1;
    public int CompensationAmount { get; set; }
    public string? CompensationReason { get; set; }
    public DateTimeOffset? ActualStartsAt { get; set; }
    public DateTimeOffset? ActualEndsAt { get; set; }
}
