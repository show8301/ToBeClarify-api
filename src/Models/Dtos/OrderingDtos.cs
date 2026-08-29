using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record OrderingSettingsDto(
    int MinimumMealCredit,
    int BaseNominationFee,
    int SegmentMinutes,
    int ReminderAfterMinutes,
    int EscalateAfterMinutes,
    int ExpireAfterMinutes,
    DateTimeOffset? NominationPausedUntil,
    bool NominationPaused);

public sealed record OrderSessionDto(
    string Id,
    string GameId,
    string CustomerName,
    DateOnly BusinessDate,
    int MaxNominatedStaff,
    int PrepaidMealCredit,
    int RemainingMealCredit,
    string Status);

public sealed record OrderSessionIssuedDto(
    OrderSessionDto Session,
    string OrderToken,
    string OrderUrl,
    string RecoveryCode);

public sealed record OrderSessionAccessDto(
    OrderSessionDto Session,
    OrderingSettingsDto Settings);

public sealed record OrderCatalogDto(
    OrderingSettingsDto Settings,
    MenuDto Menu,
    IReadOnlyList<StaffListItemDto> Staff);

public sealed record OrderItemDto(
    string Id,
    string ItemType,
    string? ReferenceId,
    string? ParentItemId,
    string Name,
    int UnitPrice,
    int Quantity,
    int? SegmentCount,
    int? DurationMinutes,
    int LineTotal,
    string? PriceRule);

public sealed record OrderNomineeDto(
    string Id,
    string StaffId,
    string StaffName,
    string ServiceId,
    string ServiceName,
    int SegmentCount,
    int ServiceDurationMinutes,
    DateTimeOffset RequestedStartsAt,
    DateTimeOffset RequestedServiceEndsAt,
    DateTimeOffset BusyUntil,
    string ConfirmationStatus,
    DateTimeOffset? ConfirmedAt);

public sealed record OrderTipDto(
    string Id,
    string? StaffId,
    string? StaffName,
    int TipAmount,
    int StaffPercentage,
    int StorePercentage,
    int StaffAmount,
    int StoreAmount);

public sealed record OrderStatusHistoryDto(
    string FromStatus,
    string ToStatus,
    string? Reason,
    string ActorType,
    DateTimeOffset CreatedAt);

public sealed record OrderDto(
    string Id,
    string OrderNumber,
    string Status,
    string QueueStage,
    int QueueMinutes,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ConfirmedAt,
    int Subtotal,
    int MealCreditApplied,
    int TotalAmount,
    string? CustomerNote,
    string? InternalNote,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderNomineeDto> Nominees,
    IReadOnlyList<OrderTipDto> Tips,
    IReadOnlyList<OrderStatusHistoryDto> History);

public sealed record AdminOrderSessionDto(
    OrderSessionDto Session,
    int OrderCount,
    int WaitingOrderCount,
    int ConfirmedOrderCount,
    int TotalAmount,
    DateTimeOffset? LastOrderedAt);

public sealed record OrderingReportSummaryDto(
    DateOnly BusinessDate,
    int OrderCount,
    int GrossAmount,
    int MealCreditApplied,
    int NetAmount,
    int StaffTipAmount,
    int StoreTipAmount);

public sealed class CreateOrderSessionRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string GameId { get; init; } = string.Empty;

    [StringLength(100)]
    public string? CustomerName { get; init; }

    [Range(0, 100)]
    public int? MaxNominatedStaff { get; init; }
}

public sealed class UpdateOrderSessionRequest
{
    [StringLength(100)]
    public string? CustomerName { get; init; }

    [Range(0, 100)]
    public int? MaxNominatedStaff { get; init; }

    [Range(0, int.MaxValue)]
    public int? RemainingMealCredit { get; init; }
}

public sealed class AccessOrderSessionRequest
{
    [Required]
    public string OrderToken { get; init; } = string.Empty;
}

public sealed class RecoverOrderSessionRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string GameId { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string RecoveryCode { get; init; } = string.Empty;
}

public sealed class MealOrderLineRequest
{
    [Required]
    public string ReferenceId { get; init; } = string.Empty;

    [Required, RegularExpression("^(item|set)$")]
    public string Kind { get; init; } = "item";

    [Range(1, 99)]
    public int Quantity { get; init; } = 1;
}

public sealed class NominationOrderLineRequest
{
    [Required]
    public string StaffId { get; init; } = string.Empty;

    [Required]
    public string ServiceId { get; init; } = string.Empty;

    [Range(1, 72)]
    public int SegmentCount { get; init; } = 1;

    [Range(1, 20)]
    public int ParticipantCount { get; init; } = 1;

    public DateTimeOffset RequestedStartsAt { get; init; }
}

public sealed class TipOrderLineRequest
{
    public string? StaffId { get; init; }

    [Range(1, 1000000)]
    public int Amount { get; init; }

    [Range(0, 100)]
    public int StaffPercentage { get; init; }
}

public sealed class SubmitOrderRequest
{
    public IReadOnlyList<MealOrderLineRequest> Meals { get; init; } = [];
    public IReadOnlyList<NominationOrderLineRequest> Nominations { get; init; } = [];
    public IReadOnlyList<TipOrderLineRequest> Tips { get; init; } = [];

    [StringLength(500)]
    public string? CustomerNote { get; init; }
}

public sealed class UpdateOrderingSettingsRequest
{
    [Range(0, 1000000)]
    public int MinimumMealCredit { get; init; }

    [Range(0, 1000000)]
    public int BaseNominationFee { get; init; }

    [Range(1, 240)]
    public int SegmentMinutes { get; init; } = 20;

    [Range(1, 1440)]
    public int ReminderAfterMinutes { get; init; } = 5;

    [Range(1, 1440)]
    public int EscalateAfterMinutes { get; init; } = 10;

    [Range(1, 1440)]
    public int ExpireAfterMinutes { get; init; } = 20;
}

public sealed class PauseNominationRequest
{
    [Range(0, 1440)]
    public int Minutes { get; init; }
}

public sealed class UpdateAdminOrderRequest
{
    [StringLength(500)]
    public string? CustomerNote { get; init; }

    [StringLength(1000)]
    public string? InternalNote { get; init; }

    [RegularExpression("^(submitted|needs_reschedule|confirmed|in_service|completed|rejected|cancelled)$")]
    public string? Status { get; init; }
}

public sealed class UpdateOrderItemRequest
{
    [StringLength(160, MinimumLength = 1)]
    public string? Name { get; init; }

    [Range(0, 1000000)]
    public int? UnitPrice { get; init; }

    [Range(1, 99)]
    public int? Quantity { get; init; }
}

public sealed class RescheduleOrderRequest
{
    public DateTimeOffset RequestedStartsAt { get; init; }
}

public sealed class OrderActionRequest
{
    [StringLength(500)]
    public string? Reason { get; init; }
}
