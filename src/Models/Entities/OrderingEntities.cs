namespace ToBeClarify.Api.Models.Entities;

public sealed class OrderingSettingsRow
{
    public int MinimumMealCredit { get; set; }
    public int BaseNominationFee { get; set; }
    public int TipPresetAmount1 { get; set; }
    public int TipPresetAmount2 { get; set; }
    public int TipPresetAmount3 { get; set; }
    public int TipPresetAmount4 { get; set; }
    public int SegmentMinutes { get; set; }
    public int ReminderAfterMinutes { get; set; }
    public int EscalateAfterMinutes { get; set; }
    public int ExpireAfterMinutes { get; set; }
    public int BusinessDayStartMinute { get; set; }
    public int BusinessDayEndMinute { get; set; }
    public bool BusinessDayEndsNextDay { get; set; } = true;
    public DateTime? NominationPausedUntil { get; set; }
}

public sealed class BusinessPeriodRow
{
    public string Id { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}

public sealed class BusinessDayOverrideRow
{
    public string Id { get; set; } = "default";
    public DateTime BusinessDate { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderSessionRow
{
    public string Id { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public string AccessTokenHash { get; set; } = string.Empty;
    public string RecoveryCodeHash { get; set; } = string.Empty;
    public int MaxNominatedStaff { get; set; }
    public int PrepaidMealCredit { get; set; }
    public int RemainingMealCredit { get; set; }
    public string SessionStatus { get; set; } = string.Empty;
    public DateTime? LastAccessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MenuProductRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class StaffOfferRow
{
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public bool IsWorkingToday { get; set; }
    public bool StaffIsNominatable { get; set; }
    public int BufferMinutes { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int? Price { get; set; }
    public int? DurationMinutes { get; set; }
    public bool ServiceIsNominatable { get; set; }
    public int? AdditionalPersonPrice { get; set; }
    public bool ServiceIsEnabled { get; set; }
}

public sealed class StaffNominationRow
{
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public bool IsWorkingToday { get; set; }
    public bool StaffIsNominatable { get; set; }
    public int BufferMinutes { get; set; }
}

public sealed class OrderRow
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderKind { get; set; } = "standard";
    public string? ParentNomineeId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public DateTime? QueueEnteredAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int Subtotal { get; set; }
    public int MealCreditApplied { get; set; }
    public int TotalAmount { get; set; }
    public string? CustomerNote { get; set; }
    public string? InternalNote { get; set; }
}

public sealed class OrderItemRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ParentItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int? SegmentCount { get; set; }
    public int? DurationMinutes { get; set; }
    public int LineTotal { get; set; }
    public string? PriceRule { get; set; }
    public int SortOrder { get; set; }
}

public sealed class OrderNomineeRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffNameSnapshot { get; set; } = string.Empty;
    public string? ServiceId { get; set; }
    public string ServiceNameSnapshot { get; set; } = string.Empty;
    public string NominationMode { get; set; } = "service";
    public int SegmentCount { get; set; }
    public int ServiceDurationMinutes { get; set; }
    public int SegmentMinutesSnapshot { get; set; }
    public int ReservedMinutes { get; set; }
    public int BufferMinutesSnapshot { get; set; }
    public DateTime RequestedStartsAt { get; set; }
    public DateTime RequestedServiceEndsAt { get; set; }
    public DateTime RequestedBusyUntil { get; set; }
    public string ConfirmationStatus { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
}

public sealed class OrderTipRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderItemId { get; set; } = string.Empty;
    public string? StaffId { get; set; }
    public string? StaffNameSnapshot { get; set; }
    public int TipAmount { get; set; }
    public int StaffPercentage { get; set; }
    public int StorePercentage { get; set; }
    public int StaffAmount { get; set; }
    public int StoreAmount { get; set; }
}

public sealed class OrderHistoryRow
{
    public string OrderId { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class OrderAddonRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ParentNomineeId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffNameSnapshot { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceNameSnapshot { get; set; } = string.Empty;
    public int SegmentCount { get; set; }
    public int ServiceDurationMinutes { get; set; }
    public int ParticipantCount { get; set; }
    public string AddonStatus { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
    public string ParentOrderStatus { get; set; } = string.Empty;
    public DateTime ParentServiceEndsAt { get; set; }
}

public sealed class AddonParentRow
{
    public string NomineeId { get; set; } = string.Empty;
    public string ParentOrderId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public string ParentOrderStatus { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime ServiceEndsAt { get; set; }
    public int SegmentMinutes { get; set; }
}

public sealed class AdminOrderSessionRow : OrderSessionRow
{
    public int OrderCount { get; set; }
    public int WaitingOrderCount { get; set; }
    public int ConfirmedOrderCount { get; set; }
    public int TotalAmount { get; set; }
    public DateTime? LastOrderedAt { get; set; }
}

public sealed record NewOrderItem(
    string Id, string ItemType, string? ReferenceId, string? ParentItemId,
    string Name, int UnitPrice, int Quantity, int? SegmentCount,
    int? DurationMinutes, int LineTotal, string? PriceRule, int SortOrder);

public sealed record NewOrderNominee(
    string Id, string StaffId, string StaffName, string? ServiceId, string ServiceName, string NominationMode,
    int SegmentCount, int ServiceDurationMinutes, int SegmentMinutesSnapshot,
    int ReservedMinutes, int BufferMinutesSnapshot, DateTime StartsAt,
    DateTime ServiceEndsAt, DateTime BusyUntil);

public sealed record NewOrderTip(
    string Id, string OrderItemId, string? StaffId, string? StaffName,
    int Amount, int StaffPercentage, int StorePercentage, int StaffAmount, int StoreAmount);

public sealed record NewOrderAggregate(
    string Id, string SessionId, string OrderNumber, string OrderKind, string? ParentNomineeId,
    string Status, DateTime? QueueEnteredAt,
    DateTime SubmittedAt, int Subtotal, int MealCreditApplied, int TotalAmount,
    string? CustomerNote, IReadOnlyList<NewOrderItem> Items,
    IReadOnlyList<NewOrderNominee> Nominees, IReadOnlyList<NewOrderTip> Tips);

public sealed record OrderBundle(
    IReadOnlyList<OrderRow> Orders,
    IReadOnlyList<OrderItemRow> Items,
    IReadOnlyList<OrderNomineeRow> Nominees,
    IReadOnlyList<OrderTipRow> Tips,
    IReadOnlyList<OrderHistoryRow> History,
    IReadOnlyList<OrderAddonRow> Addons);

public sealed record NewAddonAggregate(
    string Id, string SessionId, string OrderNumber, string ParentNomineeId, string Status,
    DateTime? QueueEnteredAt, DateTime SubmittedAt, int TotalAmount, NewOrderItem Item,
    string AddonId, string StaffId, string StaffName, string ServiceId, string ServiceName,
    int SegmentCount, int ServiceDurationMinutes, int ParticipantCount, string AddonStatus,
    string ActorType, string? ActorId, string? ActorRole);
