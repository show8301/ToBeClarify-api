using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed class SettlementSourceData
{
    public IReadOnlyList<SettlementOrderRevenueRow> Orders { get; init; } = [];
    public IReadOnlyList<SettlementOrderItemRevenueRow> Items { get; init; } = [];
    public IReadOnlyList<SettlementNomineeRevenueRow> Nominees { get; init; } = [];
    public IReadOnlyList<SettlementTipRevenueRow> Tips { get; init; } = [];
    public IReadOnlyList<SettlementRoomRevenueRow> Rooms { get; init; } = [];
    public IReadOnlyList<SettlementAddonRevenueRow> Addons { get; init; } = [];
    public IReadOnlyList<SettlementAdmissionRow> Admissions { get; init; } = [];
    public IReadOnlyList<SettlementStaffInputRow> StaffInputs { get; init; } = [];
    public IReadOnlyList<SettlementResultLineRow> Results { get; init; } = [];
    public IReadOnlyList<SettlementAttendanceBackfillRow> AttendanceBackfillRequests { get; init; } = [];
    public IReadOnlyList<SettlementRunRow> Runs { get; init; } = [];
}

public interface ISettlementRepository
{
    Task<IReadOnlyList<SettlementRuleRow>> GetRulesAsync(string? dayType, CancellationToken cancellationToken);
    Task<SettlementRuleRow?> GetEffectiveRuleAsync(string dayType, DateOnly businessDate, CancellationToken cancellationToken);
    Task InsertRuleAsync(SettlementRuleRow rule, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<SettlementRunRow?> GetRunAsync(DateOnly businessDate, int sessionNo, CancellationToken cancellationToken);
    Task<SettlementRunRow> GetOrCreateRunAsync(DateOnly businessDate, int sessionNo, string dayType,
        string? ruleVersionId, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<SettlementSourceData> GetSourceDataAsync(string settlementId, DateOnly businessDate,
        CancellationToken cancellationToken);
    Task SaveInputsAsync(string settlementId, SaveInputsData inputs, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task SaveCalculationAsync(string settlementId, SaveCalculationData calculation, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task FinalizeAsync(string settlementId, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<SettlementRunRow> ReopenAsync(DateOnly businessDate, int sessionNo, SettlementRuleRow rule,
        string reason, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<SettlementAttendanceBackfillRow> SubmitAttendanceBackfillAsync(string settlementId, string staffId,
        string role, int requestedMinutes, string reason, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task<SettlementAttendanceBackfillRow> ReviewAttendanceBackfillAsync(string requestId, bool approved,
        string? note, string actorId, DateTime now, CancellationToken cancellationToken);
    Task SaveOrderAdjustmentAsync(string settlementId, string orderId, int adjustedAmount, string reason,
        string? note, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<int?> GetOrderTotalAsync(string orderId, CancellationToken cancellationToken);
}

public sealed class SaveInputsData
{
    public string? DayType { get; init; }
    public string? RuleVersionId { get; init; }
    public int PublicTipAmount { get; init; }
    public int? AdmissionFeeOverride { get; init; }
    public int ActivityExpense { get; init; }
    public decimal? CompanyShareHours { get; init; }
    public bool ActivityHoursConfirmed { get; init; }
    public IReadOnlyList<SaveStaffInputData> StaffInputs { get; init; } = [];
}

public sealed class SaveStaffInputData
{
    public string StaffId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public int ActualMinutes { get; init; }
    public decimal? ActivityHours { get; init; }
    public bool PublicTipEligible { get; init; }
    public bool IsBackstageParticipant { get; init; }
    public string AttendanceSource { get; init; } = "manual";
    public string? Note { get; init; }
}

public sealed class SaveCalculationData
{
    public SettlementRunRow Run { get; init; } = new();
    public IReadOnlyList<SettlementResultLineRow> Results { get; init; } = [];
}
