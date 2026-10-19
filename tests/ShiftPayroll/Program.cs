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
        Orders = [new() { OrderId = "payroll", Subtotal = revenue, TotalAmount = revenue },
            new() { OrderId = "tip", Subtotal = 11, TotalAmount = 11, TipAmount = 11 }],
        Items = [new() { ItemId = "meal", OrderId = "payroll", ItemType = "menu_item", LineTotal = revenue }],
        Tips = [new() { OrderId = "tip", StaffId = "staff", StaffAmount = 11 }],
        Cash = new() { HasCashRecords = useCash, NetCash = revenue / 2m + 11 },
        CashAllocations = useCash ? [Cash("payroll", revenue / 2m), Cash("tip", 11)] : []
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

var noOrderSources = Calculate(new SettlementSourceData
{
    PayrollAssignments = [Assignment("service")], StaffInputs = [Input("service")],
    Orders = [new() { OrderId = "shared-pool", Subtotal = 1000, TotalAmount = 1000 }],
    Items = [new() { ItemId = "meal", OrderId = "shared-pool", ItemType = "menu_item", LineTotal = 1000 }]
}, new()).Single(x => x.Role == "service");
Check(noOrderSources.Sources.Count == 0, "Service shared-pool allocation does not expose individual order sources.");

var provenanceSource = new SettlementSourceData
{
    PayrollAssignments = [Assignment("designated")],
    StaffInputs = [Input("designated")],
    Orders =
    [
        new() { OrderId = "source-a", OrderNumber = "A001", Subtotal = 1010, TotalAmount = 1010, TipAmount = 10 },
        new() { OrderId = "source-b", OrderNumber = "B002", Subtotal = 1005, TotalAmount = 1005, TipAmount = 5 }
    ],
    Items =
    [
        new() { ItemId = "nomination-a", OrderId = "source-a", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 1000 },
        new() { ItemId = "nomination-b", OrderId = "source-b", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 1000 }
    ],
    Tips =
    [
        new() { OrderId = "source-a", StaffId = "staff", StaffAmount = 10 },
        new() { OrderId = "source-b", StaffId = "staff", StaffAmount = 5 }
    ],
    Cash = new() { HasCashRecords = true, NetCash = 1007.5m },
    CashAllocations = [Cash("source-a", 505), Cash("source-b", 502.5m)]
};
var provenanceResult = Calculate(provenanceSource, new()).Single(x => x.Role == "designated");
var revenueSources = provenanceResult.Sources.Where(x => x.Kind == "designated_revenue").OrderBy(x => x.OrderNumber).ToArray();
var tipSources = provenanceResult.Sources.Where(x => x.Kind == "designated_tip").OrderBy(x => x.OrderNumber).ToArray();
Check(revenueSources.Select(x => x.OrderNumber).SequenceEqual(["A001", "B002"]),
    "Designated revenue sources retain each order number.");
Equal(revenueSources.Sum(x => x.BasisAmount), provenanceResult.RevenueBase,
    "Designated order bases reconcile to the payroll revenue base after cash allocation.");
Equal(revenueSources.Sum(x => x.Amount), provenanceResult.RevenueShare,
    "Designated order shares reconcile to the payroll revenue share after cash allocation.");
Check(tipSources.Select(x => x.OrderNumber).SequenceEqual(["A001", "B002"]),
    "Designated tip sources retain each order number.");
Equal(tipSources.Sum(x => x.Amount), provenanceResult.DesignatedTip,
    "Designated tip order amounts reconcile to the payroll tip.");

// The reported 2026-10-18 order: two distinct 150,000 G items, with unrelated
// day cash that previously multiplied both bases by 28,170,000 / 22,160,000.
var isolatedOrder = new SettlementSourceData
{
    PayrollAssignments = [Assignment("designated")], StaffInputs = [Input("designated")],
    Orders = [new() { OrderId = "reported", OrderNumber = "20261018-223028-9149", Subtotal = 300000, TotalAmount = 300000 },
        new() { OrderId = "other", Subtotal = 21860000, TotalAmount = 21860000 }],
    Items = [new() { ItemId = "base", ItemName = "基礎指名費", OrderId = "reported", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 150000 },
        new() { ItemId = "service", ItemName = "簽繪拍立得", ParentItemId = "base", OrderId = "reported", ItemType = "staff_service", LineTotal = 150000 },
        new() { ItemId = "other-item", OrderId = "other", ItemType = "menu_item", LineTotal = 21860000 }],
    Cash = new() { HasCashRecords = true, NetCash = 28170000 },
    CashAllocations = [Cash("reported", 300000), Cash("other", 21860000), Cash(null, 6010000)]
};
var isolatedResult = Calculate(isolatedOrder, new()).Single(x => x.Role == "designated");
Equal(isolatedResult.RevenueBase, 300000, "Unrelated day cash cannot inflate this order's 300,000 G basis.");
Equal(isolatedResult.RevenueShare, 210000, "The order earns 300,000 G × 70%, once.");
Check(isolatedResult.Sources.Count == 2 && isolatedResult.Sources.Select(x => x.OrderItemId).Distinct().Count() == 2,
    "One order retains two distinct original item identities.");
Check(isolatedResult.Sources.All(x => x.OriginalAmount == 150000 && x.BasisAmount == 150000 && x.Amount == 105000),
    "Each source records its 150,000 G original amount, recognized basis and 105,000 G share.");
Check(Anomalies(isolatedOrder).Any(x => x.Code == "FINANCE_CASH_ALLOCATION_REQUIRED"),
    "Unallocated cash requires reconciliation instead of increasing every employee's commission.");
var unpaidOther = isolatedOrder with { CashAllocations = [Cash("reported", 300000)] };
Equal(Calculate(unpaidOther, new()).Single(x => x.Role == "designated").RevenueShare, 210000,
    "An unrelated unpaid order cannot reduce a fully paid order's commission.");
Equal(Summary(unpaidOther).MealRevenue, 0, "Unpaid order revenue is not recognized from another order's cash.");
var refundedOrder = isolatedOrder with { CashAllocations = [Cash("reported", 300000), Cash("reported", 100000, "cash_refund")] };
Equal(Calculate(refundedOrder, new()).Single(x => x.Role == "designated").RevenueShare, 140000,
    "Refunds reduce only their order's commission.");
Equal(Calculate(refundedOrder, new()).Single(x => x.Role == "designated").AfterRounding, 140000,
    "Repeating payment ratios do not add a spurious Gil when the final wage is an exact integer.");
var held = Cash("reported", 300000); held.HoldScope = "order";
Equal(Calculate(isolatedOrder with { CashAllocations = [held] }, new()).Single(x => x.Role == "designated").RevenueShare,
    0, "Held cash does not fund commission.");
var overpaidOrder = isolatedOrder with { CashAllocations = [Cash("reported", 450000)] };
Equal(Calculate(overpaidOrder, new()).Single(x => x.Role == "designated").RevenueBase, 300000,
    "Overpayments cannot increase an order beyond its charge amount.");
Check(Anomalies(overpaidOrder).Any(x => x.Code == "ORDER_CASH_RECONCILIATION_REQUIRED"),
    "Excess receipts require review before finalizing or confirming recalculation.");
var itemCashA = Cash("reported", 150000); itemCashA.SourceKind = "item"; itemCashA.OrderItemId = "base";
var itemCashB = Cash("reported", 150000); itemCashB.SourceKind = "item"; itemCashB.OrderItemId = "service";
Equal(Calculate(isolatedOrder with { CashAllocations = [itemCashA, itemCashB] }, new()).Single(x => x.Role == "designated").RevenueBase,
    300000, "Two item payments are summed once into their own order.");
var pendingCash = Cash("reported", 300000); pendingCash.AllocationStatus = "pending";
Equal(Calculate(isolatedOrder with { CashAllocations = [pendingCash] }, new()).Single(x => x.Role == "designated").RevenueShare,
    0, "Unconfirmed receipts do not fund commission.");
var wrongItemCash = Cash("reported", 300000); wrongItemCash.SourceKind = "item"; wrongItemCash.OrderItemId = "other-item";
var wrongItemSource = isolatedOrder with { CashAllocations = [wrongItemCash] };
Equal(Calculate(wrongItemSource, new()).Single(x => x.Role == "designated").RevenueShare, 0,
    "An item from another order cannot fund this order's commission.");
Check(Anomalies(wrongItemSource).Any(x => x.Code == "FINANCE_ITEM_ALLOCATION_INVALID"),
    "An inconsistent item and order association requires reconciliation.");
var chargeReduction = Cash("reported", 50000, "charge_reduce");
var unresolvedCharge = isolatedOrder with { CashAllocations = [Cash("reported", 250000), chargeReduction] };
Check(Anomalies(unresolvedCharge).Any(x => x.Code == "ORDER_CHARGE_RECONCILIATION_REQUIRED"),
    "A separate charge reduction requires an explicit matching settlement adjustment.");
var reconciledCharge = unresolvedCharge with
{
    Orders = [new() { OrderId = "reported", OrderNumber = "20261018-223028-9149",
        Subtotal = 300000, TotalAmount = 300000, AdjustedAmount = 250000 }]
};
Check(!Anomalies(reconciledCharge).Any(x => x.Code == "ORDER_CHARGE_RECONCILIATION_REQUIRED"),
    "The matching saved settlement adjustment resolves the charge difference.");
Equal(Calculate(reconciledCharge, new()).Single(x => x.Role == "designated").RevenueShare, 175000,
    "An explicitly reconciled charge reduction affects only that order's basis.");

var tipCashSource = new SettlementSourceData
{
    PayrollAssignments = [Assignment("designated")], StaffInputs = [Input("designated")],
    Orders = [new() { OrderId = "tip-order", Subtotal = 1100, TotalAmount = 1100, TipAmount = 100 }],
    Items = [new() { ItemId = "tip-base", OrderId = "tip-order", ItemType = "nomination_base", ReferenceId = "staff", LineTotal = 1000 }],
    Tips = [new() { OrderId = "tip-order", StaffId = "staff", TipAmount = 100, StaffAmount = 50, StoreAmount = 50 }],
    Cash = new() { HasCashRecords = true, NetCash = 550 }, CashAllocations = [Cash("tip-order", 550)]
};
var partialTipResult = Calculate(tipCashSource, new()).Single(x => x.Role == "designated");
Equal(partialTipResult.RevenueBase, 500, "Partial receipts recognize revenue excluding the proportional tip.");
Equal(partialTipResult.DesignatedTip, 25, "Only the paid fraction of a designated tip is payable.");
Equal(Summary(tipCashSource).CompanyIncome, 137.5m, "Store tips are company income without entering revenue-share pools.");
Equal(partialTipResult.PublicTip, 0, "Store tips do not automatically change the manual public-tip pool.");

var admissionA = Cash(null, 100); admissionA.SourceKind = "admission"; admissionA.SessionId = "A";
var chargeA = Cash(null, 50, "charge_add"); chargeA.SourceKind = "admission"; chargeA.SessionId = "A";
var chargeB = Cash(null, 50, "charge_add"); chargeB.SourceKind = "admission"; chargeB.SessionId = "B";
var admissionSource = new SettlementSourceData
{
    Admissions = [new() { UseAdmissionLedger = true, LedgerReceivable = 100 }],
    Cash = new() { HasCashRecords = true, NetCash = 100 }, CashAllocations = [admissionA, chargeA, chargeB]
};
Equal(Summary(admissionSource).AdmissionRevenue, 50, "One customer's admission overpayment does not pay another customer's admission.");
Check(Anomalies(admissionSource).Any(x => x.Code == "ADMISSION_CASH_RECONCILIATION_REQUIRED"), "Admission excess cash needs reconciliation.");

var secondAssignment = Assignment("service"); secondAssignment.StaffId = "second";
var secondInput = Input("service"); secondInput.StaffId = "second"; secondInput.ActualMinutes = 180;
var publicTipSource = new SettlementSourceData
{
    PayrollAssignments = [Assignment("designated"), secondAssignment],
    StaffInputs = [Input("designated"), secondInput]
};
var publicTipResults = Calculate(publicTipSource, new() { PublicTipAmount = 400 });
Equal(publicTipResults.Single(x => x.StaffId == "staff").PublicTip, 100, "Public tips retain the existing payable-hours weighting.");
Equal(publicTipResults.Single(x => x.StaffId == "second").PublicTip, 300, "Three payable hours receive three quarters of public tips.");
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
IReadOnlyList<SettlementAnomalyDto> Anomalies(SettlementSourceData source)
{
    var result = method.Invoke(service, [new SettlementRunRow(), rule, source])!;
    return (IReadOnlyList<SettlementAnomalyDto>)result.GetType().GetProperty("Anomalies")!.GetValue(result)!;
}
SettlementSummaryDto Summary(SettlementSourceData source)
{
    var result = method.Invoke(service, [new SettlementRunRow(), rule, source])!;
    return (SettlementSummaryDto)result.GetType().GetProperty("Summary")!.GetValue(result)!;
}
static SettlementCashAllocationRow Cash(string? orderId, decimal amount, string kind = "cash_receipt")
    => new() { Id = Guid.NewGuid().ToString("D"), SessionId = "session", SourceKind = orderId is null ? "unallocated" : "order",
        OrderId = orderId, Kind = kind, Amount = amount, AllocationStatus = "confirmed", HoldScope = "none", CashPeriodId = "period" };
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
