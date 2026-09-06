namespace ToBeClarify.Api.Models.Entities;

public sealed class SettlementRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string DayType { get; set; } = "normal";
    public DateTime EffectiveFrom { get; set; }
    public int DesignatedHourlyRate { get; set; }
    public int ServiceManagerHourlyRate { get; set; }
    public int BackstageHourlyRate { get; set; }
    public decimal DesignatedSharePercentage { get; set; }
    public decimal PublicRoomStaffPercentage { get; set; }
    public decimal DedicatedRoomOwnerPercentage { get; set; }
    public decimal ServiceManagerPoolPercentage { get; set; }
    public decimal BackstagePoolPercentage { get; set; }
    public decimal CompanyPercentage { get; set; }
    public int TimeRoundMinutes { get; set; }
    public int MoneyRoundUnit { get; set; }
    public string PublicTipMode { get; set; } = "hour_ratio";
}

public sealed class SettlementRunRow
{
    public string Id { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public int SessionNo { get; set; }
    public string DayType { get; set; } = "normal";
    public string Status { get; set; } = "draft";
    public string? RuleVersionId { get; set; }
    public string? RuleSnapshotJson { get; set; }
    public int PublicTipAmount { get; set; }
    public int? AdmissionFeeOverride { get; set; }
    public int AdmissionFeeSnapshot { get; set; }
    public int ActivityExpense { get; set; }
    public decimal? CompanyShareHours { get; set; }
    public bool ActivityHoursConfirmed { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal DesignatedRevenueBase { get; set; }
    public decimal DedicatedRoomGross { get; set; }
    public decimal DedicatedRoomOwnerShare { get; set; }
    public decimal DedicatedRoomCompanyRemainder { get; set; }
    public decimal PublicRoomUnassignedRevenue { get; set; }
    public decimal AdmissionRevenue { get; set; }
    public decimal MealRevenue { get; set; }
    public decimal CompanyRevenue { get; set; }
    public decimal ServiceManagerPool { get; set; }
    public decimal BackstagePool { get; set; }
    public decimal CompanyIncome { get; set; }
    public decimal ActivityNetRevenue { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? FinalizedBy { get; set; }
}

public sealed class SettlementStaffInputRow
{
    public string Id { get; set; } = string.Empty;
    public string SettlementId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public bool IsActive { get; set; }
    public bool IsWorking { get; set; }
    public int ActualMinutes { get; set; }
    public decimal? ActivityHours { get; set; }
    public bool PublicTipEligible { get; set; }
    public bool IsBackstageParticipant { get; set; }
    public string? Note { get; set; }
}

public sealed class SettlementResultLineRow
{
    public string Id { get; set; } = string.Empty;
    public string SettlementId { get; set; } = string.Empty;
    public string? StaffId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public decimal PayableHours { get; set; }
    public int HourlyRate { get; set; }
    public decimal BasePay { get; set; }
    public decimal RevenueBase { get; set; }
    public decimal RevenuePercentage { get; set; }
    public decimal RevenueShare { get; set; }
    public decimal DesignatedTip { get; set; }
    public decimal PublicTip { get; set; }
    public decimal PreferredPay { get; set; }
    public decimal BeforeRounding { get; set; }
    public int AfterRounding { get; set; }
    public decimal CompanySubsidy { get; set; }
    public decimal ManualAdjustment { get; set; }
    public string AnomalyStatus { get; set; } = "normal";
    public string? AnomalyNote { get; set; }
}

public sealed class SettlementOrderRevenueRow
{
    public string OrderId { get; set; } = string.Empty;
    public int TotalAmount { get; set; }
    public int Subtotal { get; set; }
    public int TipAmount { get; set; }
    public int? AdjustedAmount { get; set; }
}

public sealed class SettlementOrderItemRevenueRow
{
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ParentItemId { get; set; }
    public int LineTotal { get; set; }
}

public sealed class SettlementNomineeRevenueRow
{
    public string OrderId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
}

public sealed class SettlementTipRevenueRow
{
    public string OrderId { get; set; } = string.Empty;
    public string? StaffId { get; set; }
    public int StaffAmount { get; set; }
    public int TipAmount { get; set; }
}

public sealed class SettlementRoomRevenueRow
{
    public string Id { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? OrderItemId { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string? OwnerStaffId { get; set; }
    public string? OwnerStaffName { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class SettlementAddonRevenueRow
{
    public string OrderId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public int LineTotal { get; set; }
}

public sealed class SettlementAdmissionRow
{
    public int SessionCount { get; set; }
    public string? AdmissionFeeText { get; set; }
}
