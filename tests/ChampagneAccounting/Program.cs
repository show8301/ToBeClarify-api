using System.Reflection;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

// Isolated monetary regressions. No database, server, real orders, or external services.
var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var service = new SettlementService(null!, null!, null!, null!);
var rule = new SettlementRuleRow
{
    Id = "rules", DesignatedSharePercentage = 70, ServiceManagerPoolPercentage = 25,
    CompanyPercentage = 75, TimeRoundMinutes = 30
};
SettlementOrderItemRevenueRow Item(string id, int amount, bool champagne = false, string? recipient = null) => new()
{
    ItemId = id, OrderId = "order", ItemType = "menu_item", LineTotal = amount,
    IsChampagneTower = champagne, RecipientStaffId = recipient, RecipientStaffName = recipient
};
SettlementStaffInputRow Staff(string id, string role) => new()
{
    StaffId = id, DisplayName = id, Role = role, ActualMinutes = 60,
    ActivityHours = 1, AttendanceSource = "clock", PayrollSnapshotVersion = 1
};
(SettlementRunRow Run, IReadOnlyList<SettlementResultLineRow> Results) Calculate(
    SettlementOrderItemRevenueRow[] items, int credit = 0, bool activity = false,
    int? adjusted = null, int? netCash = null, int expense = 0)
{
    var subtotal = items.Sum(item => item.LineTotal);
    var source = new SettlementSourceData
    {
        Orders = [new() { OrderId = "order", Subtotal = subtotal, TotalAmount = subtotal - credit,
            MealCreditApplied = credit, AdjustedAmount = adjusted }],
        PayrollAssignments = new[] { ("Alice", "designated"), ("Bob", "designated"), ("Manager", "service") }
            .Select(x => new SettlementAttendanceCandidateRow { StaffId = x.Item1, PayrollRole = x.Item2, PayrollSnapshotVersion = 1 }).ToArray(),
        Items = items, StaffInputs = [Staff("Alice", "designated"), Staff("Bob", "designated"), Staff("Manager", "service")],
        Cash = new() { HasCashRecords = netCash.HasValue, NetCash = netCash ?? 0 }
    };
    var run = new SettlementRunRow
    {
        DayType = activity ? "event" : "normal", ActivityHoursConfirmed = true,
        CompanyShareHours = 1, ActivityExpense = expense
    };
    var result = calculate.Invoke(service, [run, rule, source])!;
    return ((SettlementRunRow)result.GetType().GetProperty("Run")!.GetValue(result)!,
        (IReadOnlyList<SettlementResultLineRow>)result.GetType().GetProperty("Results")!.GetValue(result)!);
}
void Equal(decimal actual, decimal expected, string name)
{
    if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
    Console.WriteLine($"PASS {name}: {actual}");
}

var store = Calculate([Item("tower", 1000, true)]);
Equal(store.Run.CompanyRevenue, 1000, "store gift enters common revenue");
Equal(store.Run.ServiceManagerPool, 250, "store gift retains common pool allocation");
Equal(store.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 0, "store gift has no direct recipient share");

var mixed = Calculate([Item("tower", 1000, true, "Alice"), Item("food", 200)], credit: 100);
Equal(mixed.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 700, "food credit does not reduce tower share");
Equal(mixed.Run.CompanyRevenue, 400, "tower remainder plus food net revenue");
Equal(mixed.Run.GrossRevenue, 1100, "mixed revenue reconciles after credit");
rule.DesignatedHourlyRate = 1000;
var wageOrder = Item("nomination", 100);
wageOrder.ItemType = "nomination_base";
wageOrder.ReferenceId = "Alice";
var wage = Calculate([Item("tower", 1000, true, "Alice"), wageOrder]);
Equal(wage.Results.Where(row => row.StaffId == "Alice").Sum(row => row.AfterRounding), 1700,
    "tower share is added above hourly wage rather than absorbed by wage comparison");
rule.DesignatedHourlyRate = 0;

var two = Calculate([Item("tower-a", 1000, true, "Alice"), Item("tower-b", 500, true, "Bob")]);
Equal(two.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 700, "first recipient attribution");
Equal(two.Results.Where(row => row.StaffId == "Bob").Sum(row => row.RevenueShare), 350, "second recipient attribution");

var reduced = Calculate([Item("tower", 500, true, "Alice"), Item("food", 200)], credit: 100);
Equal(reduced.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 350, "partially cancelled source reduces recipient share");
var removed = Calculate([Item("tower", 0, true, "Alice"), Item("food", 200)], credit: 100);
Equal(removed.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 0, "fully cancelled source has no share");

var adjustment = Calculate([Item("tower", 1000, true, "Alice"), Item("food", 200)], credit: 100, adjusted: 550);
Equal(adjustment.Results.Where(row => row.StaffId == "Alice").Sum(row => row.RevenueShare), 350, "explicit order discount reduces tower share");

var activity = Calculate([Item("tower", 1000, true, "Alice")], activity: true);
Equal(activity.Results.Single(row => row.Role == "champagne_recipient").RevenueShare, 700, "activity direct share uses designated percentage");
Equal(activity.Run.ActivityNetRevenue, 300, "activity pool excludes direct recipient share");
Equal(activity.Run.CompanyIncome, 75, "activity company allocation uses remaining pool");
Equal(activity.Results.Sum(row => row.RevenueShare) + activity.Run.CompanyIncome, 1000, "activity allocation has no double count");

var cash = Calculate([Item("tower", 1000, true, "Alice")], activity: true, netCash: 500, expense: 100);
Equal(cash.Results.Single(row => row.Role == "champagne_recipient").RevenueShare, 350, "actual net cash scales direct share");
Equal(cash.Run.ActivityNetRevenue, 50, "cash-scaled activity pool deducts fixed expense once");
Equal(cash.Run.CompanyIncome, 12.5m, "cash-scaled company share uses remaining activity pool");
Equal(cash.Results.Sum(row => row.RevenueShare) + cash.Run.CompanyIncome + 100, 500, "cash distribution reconciles with fixed activity expense");

var legacy = Calculate([Item("legacy-food", 1000)], credit: 100);
Equal(legacy.Run.MealRevenue, 900, "legacy unclassified orders retain meal accounting");
var legacyActivity = Calculate([Item("legacy-food", 1000)], activity: true, netCash: 500, expense: 100);
Equal(legacyActivity.Run.CompanyIncome, 112.5m, "legacy activity cash allocation remains unchanged");
Console.WriteLine("Champagne accounting regressions passed.");
