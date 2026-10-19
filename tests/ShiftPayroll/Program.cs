using System.Reflection;
using ToBeClarify.Api.Models;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Repositories.Shared;
using ToBeClarify.Api.Services.Admin.Settlement;

// Monetary and authorization regressions, entirely in memory.
var method = typeof(SettlementService).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Instance)!;
var service = new SettlementService(null!, null!, null!, null!);
var rule = new SettlementRuleRow
{
    DesignatedHourlyRate = 100, ServiceManagerHourlyRate = 100, BackstageHourlyRate = 1000,
    DesignatedSharePercentage = 70, ServiceManagerPoolPercentage = 25, CompanyPercentage = 75,
    TimeRoundMinutes = 30
};

foreach (var role in new[] { "service", "manager", "designated", "backstage" })
{
    var assignment = Assignment(role, role == "backstage");
    var input = Input(role);
    var source = new SettlementSourceData
    {
        PayrollAssignments = [assignment], StaffInputs = [input],
        Orders = [new() { OrderId = "order", Subtotal = 1011, TotalAmount = 1011, TipAmount = 11 }],
        Items = [new() { ItemId = "nomination", OrderId = "order", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 1000 }],
        Tips = [new() { OrderId = "order", StaffId = "staff", StaffAmount = 11 }]
    };
    var result = Calculate(source, new() { PublicTipAmount = role == "backstage" ? 0 : 19 });
    var primary = result.Single(x => x.Role == role);
    Equal(primary.BasePay, role == "backstage" ? 0 : 100, $"{role}: a single primary base wage");
    Equal(result.Sum(x => x.DesignatedTip), 11, $"{role}: designated tip once");
    Equal(result.Sum(x => x.PublicTip), role == "backstage" ? 0 : 19, $"{role}: public tips once");
    Equal(result.Where(x => x.Role == "support_service_share").Sum(x => x.RevenueShare), role == "designated" ? 0 : 700,
        $"{role}: direct support commission is separate from primary pay");
    Equal(result.Sum(x => x.AfterRounding), role == "designated" ? 730 : role == "backstage" ? 711 : 905,
        $"{role}: payable total");
}

foreach (var role in new[] { "service", "manager", "designated" })
foreach (var dayType in new[] { "normal", "event" })
foreach (var revenue in new[] { 0, 201, 2001 })
foreach (var useCash in new[] { false, true })
{
    var source = new SettlementSourceData
    {
        PayrollAssignments = [Assignment(role)], StaffInputs = [Input(role)],
        Orders = [new() { OrderId = "payroll", Subtotal = revenue, TotalAmount = revenue }],
        Items = [new() { ItemId = "meal", OrderId = "payroll", ItemType = "menu_item", LineTotal = revenue }],
        Tips = [new() { StaffId = "staff", StaffAmount = 11 }],
        Cash = new() { HasCashRecords = useCash, NetCash = revenue / 2m }
    };
    var result = Calculate(source, new()
    {
        DayType = dayType, ActivityHoursConfirmed = true, CompanyShareHours = 1, PublicTipAmount = 19
    }).Single(x => x.Role == role);
    var allocatedRevenue = revenue * (useCash ? 0.5m : 1m);
    var share = dayType == "event" ? allocatedRevenue / 2m
        : role == "designated" ? 0 : allocatedRevenue / 4m;
    var pay = role is "service" or "manager" ? 100 + share : Math.Max(100, share);
    var context = $"{role}, {dayType}, revenue {revenue}, cash {useCash}";
    Equal(result.BasePay, 100, $"{context}: hourly wage remains intact");
    Equal(result.RevenueShare, share, $"{context}: revenue allocation");
    Equal(result.PreferredPay, pay, $"{context}: role-specific wage formula");
    Equal(result.BeforeRounding, pay + 30, $"{context}: tips added once");
    Equal(result.AfterRounding, Math.Ceiling(pay + 30), $"{context}: round up after adding all pay");
    Equal(result.CompanySubsidy, Math.Ceiling(pay + 30) - (pay + 30), $"{context}: rounding subsidy");
}

var developer = Assignment("service", true);
var eventSource = new SettlementSourceData
{
    PayrollAssignments = [developer],
    StaffInputs = [Input("service"), Input("backstage")],
    Tips = [new() { StaffId = "staff", StaffAmount = 13 }],
    Orders = [new() { OrderId = "event", Subtotal = 900, TotalAmount = 900 }],
    Items = [new() { OrderId = "event", ItemId = "food", ItemType = "menu_item", LineTotal = 900 }]
};
var eventResults = Calculate(eventSource, new() { DayType = "event", ActivityHoursConfirmed = true, CompanyShareHours = 1, PublicTipAmount = 20 });
Equal(eventResults.Sum(x => x.BasePay), 100, "event: developer participation does not add a second hourly wage");
Equal(eventResults.Sum(x => x.DesignatedTip), 13, "event: designated tip is not repeated on backstage participation");
Equal(eventResults.Sum(x => x.PublicTip), 20, "event: public tips once");
Equal(eventResults.Sum(x => x.RevenueShare), 600, "event: existing activity weighting is retained");
Equal(eventResults.Sum(x => x.AfterRounding), 733, "event: primary hourly wage adds to activity share without duplicating backstage wages or tips");
eventSource.StaffInputs[0].ActivityHours = 0;
eventSource.StaffInputs[1].ActivityHours = 0;
Equal(Calculate(eventSource, new() { DayType = "event", ActivityHoursConfirmed = true, CompanyShareHours = 1 })
    .Sum(x => x.DesignatedTip), 13, "event: zero activity allocation must not discard a person's tip");

var missingInputSource = new SettlementSourceData
{
    PayrollAssignments = [new() { StaffId = "staff", DisplayName = "測試店員", PayrollRole = "service", PayrollSnapshotVersion = 1 }],
    Orders = [new() { OrderId = "service-order", OrderNumber = "S001", Subtotal = 1000, TotalAmount = 1000 },
        new() { OrderId = "tip-order", OrderNumber = "T002", Subtotal = 10, TotalAmount = 10 }],
    Items = [new() { ItemId = "service", OrderId = "service-order", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 1000 }],
    Tips = [new() { OrderId = "service-order", StaffId = "staff", StaffAmount = 3 },
        new() { OrderId = "tip-order", StaffId = "staff", StaffAmount = 7 }],
    AttendanceAnomalies = [new("PAYROLL_ROLE_UNRESOLVED", "待確認職位", "staff", "測試店員")]
};
var missingCalculation = method.Invoke(service, [new SettlementRunRow(), rule, missingInputSource])!;
var missingAnomalies = (IReadOnlyList<SettlementAnomalyDto>)missingCalculation.GetType().GetProperty("Anomalies")!.GetValue(missingCalculation)!;
var missing = missingAnomalies.Single(x => x.Code == "PAYROLL_INPUT_MISSING");
Check(missing.StaffName == "測試店員", "Missing payroll input retains the payroll assignment name without an input or nominee.");
Check(missing.Orders is { Count: 2 } && missing.Orders.Select(x => x.OrderNumber).SequenceEqual(["S001", "T002"]),
    "Revenue and tip source orders are combined, deduplicated and sorted.");
Check(missingAnomalies.Single(x => x.Code == "PAYROLL_ROLE_UNRESOLVED").Orders is { Count: 2 },
    "Attendance anomalies also retain related orders.");
Equal(Calculate(missingInputSource, new()).Sum(x => x.DesignatedTip), 10, "Diagnostic enrichment preserves designated tips.");

var rows = new List<SettlementStaffInputRow>();
var fixedRole = Assignment("service");
fixedRole.ScheduledRolesJson = "[\"designated\",\"backstage\"]";
fixedRole.ActualMinutes = 60;
Check(SettlementAttendanceSync.MergeMissingInputs("run", rows, [fixedRole]).Count == 0, "Support choices do not change the locked payroll role.");
Check(rows.Count == 1 && rows[0].Role == "service", "A clerk does not receive an unauthorized backstage row.");
fixedRole.PayrollRole = null;
Check(SettlementAttendanceSync.MergeMissingInputs("run", [], [fixedRole]).Any(x => x.Code == "PAYROLL_ROLE_UNRESOLVED"),
    "Missing historic role is unresolved instead of inferred from support.");
fixedRole.PayrollRole = "service";
rows[0].PayrollSnapshotVersion = 0;
Check(SettlementAttendanceSync.MergeMissingInputs("run", rows, [fixedRole]).Any(x => x.Code == "PAYROLL_SNAPSHOT_CHANGED"),
    "An old input version is visibly stale.");
Check(!StaffWorkRoles.AllowedSupportRoles(true).Contains("backstage"), "Ordinary staff cannot select backstage support.");
Check(StaffWorkRoles.AllowedSupportRoles(true, true).Contains("backstage"), "Developers can select backstage support.");

var snapshot = new PayrollSnapshotRow { StaffId = "staff", PayrollRole = "service", Version = 2 };
SettlementPayrollValidation.Validate(snapshot, [("staff", "service", 2)]);
Reject(() => SettlementPayrollValidation.Validate(snapshot, [("staff", "service", 1)]), "Old snapshot version is rejected.");
Reject(() => SettlementPayrollValidation.Validate(snapshot, [("staff", "designated", 2)]), "Another primary role is rejected.");
Reject(() => SettlementPayrollValidation.Validate(snapshot, [("staff", "service", 2), ("staff", "manager", 2)]), "Two primary roles are rejected.");
Reject(() => SettlementPayrollValidation.Validate(snapshot, [("staff", "service", 2), ("staff", "backstage", 2)]), "Clerk backstage participation is rejected.");
snapshot.CanUseBackstage = true;
SettlementPayrollValidation.Validate(snapshot, [("staff", "service", 2), ("staff", "backstage", 2)]);
Console.WriteLine("Shift payroll regressions passed.");

IReadOnlyList<SettlementResultLineRow> Calculate(SettlementSourceData source, SettlementRunRow run)
{
    var result = method.Invoke(service, [run, rule, source])!;
    return (IReadOnlyList<SettlementResultLineRow>)result.GetType().GetProperty("Results")!.GetValue(result)!;
}
static SettlementAttendanceCandidateRow Assignment(string role, bool developer = false)
    => new() { StaffId = "staff", DisplayName = "staff", PayrollRole = role, PayrollSnapshotVersion = 1, CanUseBackstage = developer };
static SettlementStaffInputRow Input(string role)
    => new() { StaffId = "staff", DisplayName = "staff", Role = role, ActualMinutes = 60, ActivityHours = 1,
        PayrollSnapshotVersion = 1, AttendanceSource = "clock", IsBackstageParticipant = role == "backstage" };
static void Equal(decimal actual, decimal expected, string message)
    => Check(actual == expected, $"{message}: expected {expected}, got {actual}");
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception ex) when (ex is ToBeClarify.Api.Exceptions.BusinessException or ToBeClarify.Api.Exceptions.ConflictException) { return; }
    throw new InvalidOperationException(message);
}
