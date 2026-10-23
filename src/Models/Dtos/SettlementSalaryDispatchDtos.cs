using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record SettlementSalaryDispatchDto(
    DateOnly BusinessDate, int SessionNo, string? SettlementId, bool Finalized,
    int CorrectionVersion, int InputVersion, IReadOnlyList<SettlementSalaryDispatchItemDto> Items);

public sealed record SettlementSalaryDispatchItemDto(
    string StaffId, string DisplayName, long Salary, bool IsPaid, int Version,
    DateTimeOffset? UpdatedAt, string? UpdatedBy, string? UpdatedByName);

public sealed class SaveSettlementSalaryDispatchRequest
{
    [Required] public DateOnly BusinessDate { get; init; }
    [Range(1, int.MaxValue)] public int SessionNo { get; init; } = 1;
    [Required, StringLength(36)] public string SettlementId { get; init; } = string.Empty;
    [Required, StringLength(36)] public string StaffId { get; init; } = string.Empty;
    [Required] public bool? IsPaid { get; init; }
    [Required, Range(0, int.MaxValue)] public int? ExpectedVersion { get; init; }
    [Required, Range(0, int.MaxValue)] public int? ExpectedCorrectionVersion { get; init; }
    [Required, Range(0, int.MaxValue)] public int? ExpectedInputVersion { get; init; }
    [Required, StringLength(36)] public string OperationId { get; init; } = string.Empty;
}
