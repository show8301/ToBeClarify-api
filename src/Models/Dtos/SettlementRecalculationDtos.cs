using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed class PreviewSettlementRecalculationRequest : SettlementCalculateRequest
{
    public SettlementRecalculationAdjustmentsRequest? Adjustments { get; init; }
}

public sealed class ConfirmSettlementRecalculationRequest : SettlementCalculateRequest
{
    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string SourceToken { get; init; } = string.Empty;
    [Range(0, int.MaxValue)] public int ExpectedCorrectionVersion { get; init; }
    [Required, StringLength(36, MinimumLength = 36)] public string OperationId { get; init; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; init; } = string.Empty;
    public SettlementRecalculationAdjustmentsRequest? Adjustments { get; init; }
}

public sealed class SettlementRecalculationAdjustmentsRequest
{
    [StringLength(36)] public string? RuleVersionId { get; init; }
    [Required, RegularExpression("^(normal|event)$")] public string DayType { get; init; } = "normal";
    [StringLength(500)] public string? PublicTipReconciliationReason { get; init; }
    [Range(0, int.MaxValue)] public int PublicTipAmount { get; init; }
    [Range(0, int.MaxValue)] public int? AdmissionFeeOverride { get; init; }
    [Range(0, int.MaxValue)] public int ActivityExpense { get; init; }
    [Range(0, 100000)] public decimal? CompanyShareHours { get; init; }
    public bool ActivityHoursConfirmed { get; init; }
    public List<SettlementRecalculationStaffInputAdjustmentRequest> StaffInputs { get; init; } = [];
    public List<SettlementRecalculationStaffInputKeyRequest> RemovedStaffInputs { get; init; } = [];
}

public sealed class SettlementRecalculationStaffInputAdjustmentRequest
{
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required, RegularExpression("^(designated|service|manager|backstage)$")] public string Role { get; init; } = "designated";
    public int? PayrollSnapshotVersion { get; init; }
    [Range(0, 1000000)] public int ActualMinutes { get; init; }
    [Range(0, 100000)] public decimal? ActivityHours { get; init; }
    public bool IsBackstageParticipant { get; init; }
    public bool ActualMinutesChanged { get; init; }
    public bool ActivityHoursChanged { get; init; }
    public bool BackstageParticipantChanged { get; init; }
}

public sealed class SettlementRecalculationStaffInputKeyRequest
{
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required, RegularExpression("^(designated|service|manager|backstage)$")] public string Role { get; init; } = "designated";
}

public sealed record SettlementRecalculationChangeDto(string? StaffId, string DisplayName, string Role,
    int? PreviousMinutes, int? CorrectedMinutes, decimal PreviousHours, decimal CorrectedHours,
    decimal PreviousPay, decimal CorrectedPay, decimal PayDelta,
    decimal? PreviousActivityHours = null, decimal? CorrectedActivityHours = null);

public sealed record SettlementRecalculationBalanceDto(string StaffId, string DisplayName,
    decimal CorrectedPay, decimal NetPaid, decimal ManualCorrection, decimal Outstanding);

public sealed record SettlementRecalculationPreviewDto(DateOnly BusinessDate, int SessionNo,
    string SourceToken, int CorrectionVersion, bool HasChanges,
    decimal PreviousPayroll, decimal CorrectedPayroll, decimal PayrollDelta,
    decimal PreviousNetCash, decimal CorrectedNetCash,
    decimal PreviousCompanyIncome, decimal CorrectedCompanyIncome,
    IReadOnlyList<SettlementRecalculationChangeDto> Changes,
    IReadOnlyList<SettlementRecalculationBalanceDto> Balances,
    IReadOnlyList<SettlementAnomalyDto> Anomalies,
    IReadOnlyList<SettlementRecalculationExistingCorrectionDto> ExistingCorrections,
    string? PreviousRuleVersionId = null, string? CorrectedRuleVersionId = null,
    DateOnly? PreviousRuleEffectiveFrom = null, DateOnly? CorrectedRuleEffectiveFrom = null);

public sealed record SettlementRecalculationExistingCorrectionDto(string? StaffId, long AmountDelta,
    string Reason);

public sealed record SettlementRecalculationHistoryDto(string Id, int Version,
    decimal PreviousPayroll, decimal CorrectedPayroll, decimal PayrollDelta,
    string Reason, string ActorId, DateTimeOffset CreatedAt);
