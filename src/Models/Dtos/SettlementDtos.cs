using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record SettlementRuleDto(
    string Id,
    string DayType,
    DateOnly EffectiveFrom,
    int DesignatedHourlyRate,
    int ServiceManagerHourlyRate,
    int BackstageHourlyRate,
    decimal DesignatedSharePercentage,
    decimal PublicRoomStaffPercentage,
    decimal DedicatedRoomOwnerPercentage,
    decimal ServiceManagerPoolPercentage,
    decimal BackstagePoolPercentage,
    decimal CompanyPercentage,
    int TimeRoundMinutes,
    int MoneyRoundUnit,
    string PublicTipMode);

public sealed class SaveSettlementRuleRequest
{
    [Required, RegularExpression("^(normal|event)$")]
    public string DayType { get; init; } = "normal";

    [Required]
    public DateOnly EffectiveFrom { get; init; }

    [Range(0, int.MaxValue)] public int DesignatedHourlyRate { get; init; } = 30000;
    [Range(0, int.MaxValue)] public int ServiceManagerHourlyRate { get; init; } = 50000;
    [Range(0, int.MaxValue)] public int BackstageHourlyRate { get; init; }
    [Range(0, 100)] public decimal DesignatedSharePercentage { get; init; } = 70;
    [Range(0, 100)] public decimal PublicRoomStaffPercentage { get; init; } = 70;
    [Range(0, 100)] public decimal DedicatedRoomOwnerPercentage { get; init; } = 100;
    [Range(0, 100)] public decimal ServiceManagerPoolPercentage { get; init; } = 25;
    [Range(0, 100)] public decimal BackstagePoolPercentage { get; init; } = 25;
    [Range(0, 100)] public decimal CompanyPercentage { get; init; } = 50;
    [Range(1, 1440)] public int TimeRoundMinutes { get; init; } = 30;
    [Range(1, int.MaxValue)] public int MoneyRoundUnit { get; init; } = 1;
    [Required, RegularExpression("^hour_ratio$")] public string PublicTipMode { get; init; } = "hour_ratio";
}

public sealed record SettlementRunDto(
    string Id,
    DateOnly BusinessDate,
    int SessionNo,
    string DayType,
    string Status,
    string? RuleVersionId,
    int PublicTipAmount,
    int? AdmissionFeeOverride,
    int AdmissionFeeSnapshot,
    int ActivityExpense,
    decimal? CompanyShareHours,
    bool ActivityHoursConfirmed,
    DateTime? FinalizedAt,
    string? BusinessPeriodId = null,
    long SourceVersion = 0,
    DateTime? SourceCutoffAt = null,
    decimal CashReceived = 0,
    decimal CashRefunded = 0,
    decimal NetCash = 0,
    decimal RetainedAmount = 0,
    int PendingFinanceCount = 0,
    string? CorrectsSettlementId = null,
    int CorrectionVersion = 0);

public sealed record SettlementSummaryDto(
    decimal GrossRevenue,
    decimal DesignatedRevenueBase,
    decimal DedicatedRoomGross,
    decimal DedicatedRoomOwnerShare,
    decimal DedicatedRoomCompanyRemainder,
    decimal PublicRoomUnassignedRevenue,
    decimal AdmissionRevenue,
    decimal MealRevenue,
    decimal CompanyRevenue,
    decimal ServiceManagerPool,
    decimal BackstagePool,
    decimal CompanyIncome,
    decimal ActivityNetRevenue,
    decimal TotalPayroll,
    decimal CompanySubsidy,
    int OrderCount = 0,
    int SessionCount = 0,
    decimal CashReceived = 0,
    decimal CashRefunded = 0,
    decimal NetCash = 0,
    decimal RetainedAmount = 0,
    int PendingFinanceCount = 0);

public sealed record SettlementStaffInputDto(
    string StaffId,
    string DisplayName,
    string? RoleTitle,
    string Role,
    bool IsWorking,
    int ActualMinutes,
    decimal? ActivityHours,
    decimal PayableHours,
    bool PublicTipEligible,
    bool IsBackstageParticipant,
    string? Note,
    string AttendanceSource,
    string? AttendanceRequestId,
    string? AttendanceBackfillStatus,
    string? AttendanceBackfillReason,
    string? AttendanceApprovedBy,
    DateTime? AttendanceApprovedAt);

public sealed record SettlementAttendanceBackfillDto(
    string Id,
    string StaffId,
    string StaffName,
    string Role,
    int RequestedMinutes,
    string Reason,
    string Status,
    string RequestedBy,
    DateTime RequestedAt,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? ReviewNote);

public sealed record SettlementResultLineDto(
    string? StaffId,
    string? DisplayName,
    string Role,
    decimal PayableHours,
    int HourlyRate,
    decimal BasePay,
    decimal RevenueBase,
    decimal RevenuePercentage,
    decimal RevenueShare,
    decimal DesignatedTip,
    decimal PublicTip,
    decimal PreferredPay,
    decimal BeforeRounding,
    int AfterRounding,
    decimal CompanySubsidy,
    decimal ManualAdjustment,
    string AnomalyStatus,
    string? AnomalyNote);

public sealed record SettlementAnomalyDto(string Code, string Message, string? SourceId = null);

public sealed record SettlementOverviewDto(
    SettlementRunDto Run,
    SettlementRuleDto Rule,
    SettlementSummaryDto Summary,
    IReadOnlyList<SettlementStaffInputDto> StaffInputs,
    IReadOnlyList<SettlementResultLineDto> Results,
    IReadOnlyList<SettlementAnomalyDto> Anomalies,
    IReadOnlyList<SettlementAttendanceBackfillDto> AttendanceBackfillRequests = null!,
    SettlementWorkflowDto? Workflow = null);

public sealed record SettlementWorkflowDto(
    string? BusinessPeriodId,
    string PeriodStatus,
    string IntakeMode,
    int UnfinishedOrderCount,
    int ActiveServiceCount,
    decimal CashReceived,
    decimal CashRefunded,
    decimal NetCash,
    decimal RetainedAmount,
    int PendingFinanceCount,
    bool CanStopNewOrders,
    bool CanClose,
    bool CanFinalize,
    bool CanCarryForward);

public sealed class SaveSettlementInputsRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [RegularExpression("^(normal|event)$")] public string? DayType { get; init; }
    [Range(0, int.MaxValue)] public int PublicTipAmount { get; init; }
    [Range(0, int.MaxValue)] public int? AdmissionFeeOverride { get; init; }
    [Range(0, int.MaxValue)] public int ActivityExpense { get; init; }
    [Range(0, 100000)] public decimal? CompanyShareHours { get; init; }
    public bool ActivityHoursConfirmed { get; init; }
    public List<SaveSettlementStaffInputRequest> StaffInputs { get; init; } = [];
}

public sealed class SaveSettlementStaffInputRequest
{
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required, RegularExpression("^(designated|service|manager|backstage)$")] public string Role { get; init; } = "designated";
    [Range(0, 1000000)] public int ActualMinutes { get; init; }
    [Range(0, 100000)] public decimal? ActivityHours { get; init; }
    public bool PublicTipEligible { get; init; } = true;
    public bool IsBackstageParticipant { get; init; }
    [RegularExpression("^(manual|clock|backfill_approved)$")]
    public string AttendanceSource { get; init; } = "manual";
    [StringLength(500)] public string? Note { get; init; }
}

public sealed class SettlementAttendanceBackfillRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [RegularExpression("^(normal|event)$")] public string DayType { get; init; } = "event";
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required, RegularExpression("^(designated|service|manager|backstage)$")] public string Role { get; init; } = "designated";
    [Range(1, 1000000)] public int RequestedMinutes { get; init; }
    [Required, StringLength(1000, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
}

public sealed class SettlementAttendanceReviewRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    public bool Approved { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
}

public class SettlementCalculateRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
}

public sealed class SettlementFinalizeRequest : SettlementCalculateRequest
{
    [StringLength(1000)] public string? Reason { get; init; }
}

public sealed class SettlementReopenRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [Required, StringLength(1000, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
}

public sealed class SettlementOrderAdjustmentRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [Range(0, int.MaxValue)] public int AdjustedAmount { get; init; }
    [Required, StringLength(1000, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
    [StringLength(1000)] public string? Note { get; init; }
}

public sealed class SettlementCloseRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [StringLength(80)] public string? OperationId { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}

public sealed class SettlementPayoutRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required, RegularExpression("^(payout|recovery)$")] public string EventKind { get; init; } = "payout";
    [Range(1, long.MaxValue)] public long Amount { get; init; }
    [Required, StringLength(80, MinimumLength = 8)] public string OperationId { get; init; } = string.Empty;
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
}

public sealed class SettlementCorrectionRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [Required, StringLength(32)] public string SourceKind { get; init; } = "finance";
    [StringLength(36)] public string? SourceId { get; init; }
    [StringLength(36)] public string? StaffId { get; init; }
    [Range(long.MinValue, long.MaxValue)] public long AmountDelta { get; init; }
    [Required, StringLength(80, MinimumLength = 8)] public string OperationId { get; init; } = string.Empty;
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; init; } = string.Empty;
}

public sealed record SettlementPaymentDto(string Id, string SettlementId, string StaffId, string EventKind,
    long Amount, string OperationId, string Reason, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record SettlementCorrectionDto(string Id, string SettlementId, string? CorrectionOfId,
    string SourceKind, string? SourceId, string? StaffId, long AmountDelta, string Status,
    string OperationId, string Reason, DateTimeOffset CreatedAt, string CreatedBy);
