using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed class ConfirmSettlementRecalculationRequest : SettlementCalculateRequest
{
    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string SourceToken { get; init; } = string.Empty;
    [Range(0, int.MaxValue)] public int ExpectedCorrectionVersion { get; init; }
    [Required, StringLength(36, MinimumLength = 36)] public string OperationId { get; init; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; init; } = string.Empty;
}

public sealed record SettlementRecalculationChangeDto(string? StaffId, string DisplayName, string Role,
    int? PreviousMinutes, int? CorrectedMinutes, decimal PreviousHours, decimal CorrectedHours,
    decimal PreviousPay, decimal CorrectedPay, decimal PayDelta);

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
    IReadOnlyList<SettlementRecalculationExistingCorrectionDto> ExistingCorrections);

public sealed record SettlementRecalculationExistingCorrectionDto(string? StaffId, long AmountDelta,
    string Reason);

public sealed record SettlementRecalculationHistoryDto(string Id, int Version,
    decimal PreviousPayroll, decimal CorrectedPayroll, decimal PayrollDelta,
    string Reason, string ActorId, DateTimeOffset CreatedAt);
