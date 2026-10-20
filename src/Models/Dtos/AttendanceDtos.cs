using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record StaffAttendanceEventDto(
    string Id, string StaffId, string BusinessDate, string? DutyPlanId, string EventType,
    DateTimeOffset EventAt, DateTimeOffset? EventEndAt, string Source, int MinutesDelta,
    string? BaseEventId, string OperationId, string? Reason, DateTimeOffset CreatedAt);

public sealed record StaffAttendanceSummaryDto(
    string StaffId, string DisplayName, string BusinessDate, string? DutyPlanId,
    DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd,
    DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, int ScheduledMinutes,
    int WorkedMinutes, int AdjustmentMinutes, int EffectiveMinutes, bool HasOpenShift,
    bool StopAcceptingNewOrders, int ActiveServiceCount,
    IReadOnlyList<StaffAttendanceAffectedOrderDto> AffectedOrders,
    IReadOnlyList<StaffAttendanceEventDto> Events,
    string? PayrollRole = null, int? PayrollSnapshotVersion = null,
    bool CanUseBackstage = false, bool CanUseManagerPayrollRole = false);

public sealed record StaffAttendanceAffectedOrderDto(
    string OrderId, string OrderNumber, string OrderStatus, string? CustomerName,
    DateTimeOffset RequestedStart, DateTimeOffset RequestedEnd);

public sealed record StaffAttendanceOverviewDto(
    string BusinessDate, DateTimeOffset GeneratedAt, IReadOnlyList<StaffAttendanceSummaryDto> Staff);

public sealed class StaffAttendanceActionRequest
{
    [Required, StringLength(36)] public string OperationId { get; init; } = "";
    [Required, StringLength(10)] public string BusinessDate { get; init; } = "";
    [StringLength(36)] public string? StaffId { get; init; }
    [Required, RegularExpression("^(clock_in|clock_out|add_minutes|subtract_minutes|set_times|confirm_payroll_role|stop_orders|resume_orders)$")]
    public string Action { get; init; } = "clock_in";
    [Range(1, int.MaxValue)] public int? ExpectedPayrollSnapshotVersion { get; init; }
    [RegularExpression("^(service|designated|manager|backstage)$")] public string? PayrollRole { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    [Range(1, 1000000)] public int? Minutes { get; init; }
    [StringLength(500)] public string? Reason { get; init; }
}
