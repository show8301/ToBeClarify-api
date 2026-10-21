using System.Reflection;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

static class MealCreditChecks
{
    public static void Run()
    {
        var checks = 0;
        var rule = new SettlementRuleRow { TimeRoundMinutes = 30, DesignatedSharePercentage = 70,
            ServiceManagerPoolPercentage = 25, BackstagePoolPercentage = 25, CompanyPercentage = 50,
            PublicRoomStaffPercentage = 70, DedicatedRoomOwnerPercentage = 100 };
        var original = new SettlementSourceData {
            Orders = [new() { OrderId = "order", OrderNumber = "reported", Subtotal = 160000,
                TotalAmount = 150000, MealCreditApplied = 10000 }],
            Items = [Item("base", "nomination_base", 150000), Item("meal", "menu_item", 10000)],
            StaffInputs = [new() { StaffId = "staff", Role = "designated", ActualMinutes = 60,
                ActivityHours = 1, AttendanceSource = "clock" }],
            PayrollAssignments = [new() { StaffId = "staff", PayrollRole = "designated" }],
            Cash = new() { HasCashRecords = true }, CashAllocations = [Cash(150000)]
        };
        var full = Calculate(original);
        Check(full.Line.RevenueBase == 150000 && full.Line.RevenueShare == 105000, "reported nomination retains 150,000 basis and 105,000 share");
        Check(full.Run.MealRevenue == 0 && full.Run.CompanyRevenue == 45000 && full.Run.GrossRevenue == 150000,
            "food credit moves only meal revenue, all recognized order revenue reconciles");
        Check(full.Run.ServiceManagerPool == 11250 && full.Run.BackstagePool == 11250 && full.Run.CompanyIncome == 22500,
            "all downstream pools use the corrected company base");
        var detail = full.Line.Sources.Single();
        Check(detail.OriginalAmount == 150000 && detail.CancelledAmount == 0 && detail.MealCreditAmount == 0
            && detail.AfterCreditAmount == 150000 && detail.OrderRevenueAmount == 150000
            && detail.AdjustedRevenueAmount == 150000 && detail.OrderCollectedAmount == 150000,
            "source snapshot explains why meal credit never reduces nomination");
        Check(full.Run.CalculationVersion == 5 && original.Items[0].LineTotal == 150000,
            "new formula is versioned and does not mutate source orders");
        var serviceItem = Item("service", "staff_service", 150000); serviceItem.ParentItemId = "base";
        var services = Calculate(original with {
            Orders = [new() { OrderId = "order", Subtotal = 310000, TotalAmount = 300000, MealCreditApplied = 10000 }],
            Items = [Item("base", "nomination_base", 150000), serviceItem, Item("meal", "menu_item", 10000)],
            CashAllocations = [Cash(300000)] });
        Check(services.Line.RevenueBase == 300000 && services.Line.RevenueShare == 210000
            && services.Line.Sources.Count == 2 && services.Line.Sources.All(x => x.MealCreditAmount == 0),
            "base nomination and child service are each recognized once without meal credit");
        var addon = Item("addon", "staff_service_addon", 50000); addon.OriginalLineTotal = 100000;
        var addons = Calculate(original with {
            Orders = [new() { OrderId = "order", Subtotal = 60000, TotalAmount = 50000, MealCreditApplied = 10000 }],
            Items = [addon, Item("meal", "menu_item", 10000)], CashAllocations = [Cash(50000)],
            Addons = [new() { OrderId = "order", OrderItemId = "addon", StaffId = "staff", LineTotal = 100000 }] });
        Check(addons.Line.RevenueBase == 50000 && addons.Line.RevenueShare == 35000
            && addons.Line.Sources.Single().CancelledAmount == 50000,
            "addon calculation uses the effective item instead of its pre-cancellation aggregate");
        var roomSource = original with {
            Orders = [new() { OrderId = "order", Subtotal = 280000, TotalAmount = 270000, MealCreditApplied = 10000 }],
            Items = [Item("base", "nomination_base", 150000), Item("room", "room_service", 120000), Item("meal", "menu_item", 10000)],
            Nominees = [new() { OrderId = "order", StaffId = "staff" }], CashAllocations = [Cash(270000)],
            Rooms = [new() { OrderId = "order", OrderItemId = "room", TotalAmount = 120000 }] };
        var room = Calculate(roomSource);
        Check(room.Line.RevenueBase == 270000 && room.Line.RevenueShare == 189000 && room.Run.GrossRevenue == 270000,
            "public room charges are never discounted by meal credit");
        roomSource.Rooms[0].OwnerStaffId = "owner";
        Check(Calculate(roomSource).Run.DedicatedRoomGross == 120000
            && Calculate(roomSource).Run.DedicatedRoomOwnerShare == 120000,
            "dedicated room ownership uses the same protected post-credit basis");
        var support = Calculate(original with {
            PayrollAssignments = [new() { StaffId = "staff", PayrollRole = "service" }],
            StaffInputs = [new() { StaffId = "staff", Role = "service", ActualMinutes = 60, AttendanceSource = "clock" }] }, support: true);
        Check(support.Line.Role == "support_service_share" && support.Line.Sources.Single().BasisAmount == 150000,
            "support commission retains its item source without duplicating primary tips");
        var partial = Calculate(original with { CashAllocations = [Cash(75000)] });
        Check(partial.Line.RevenueBase == 75000 && partial.Line.RevenueShare == 52500 && partial.Run.GrossRevenue == 75000,
            "partial receipts scale the post-credit values once");
        var refunded = Calculate(original with { CashAllocations = [Cash(150000), Cash(50000, "cash_refund")] });
        Check(refunded.Line.RevenueBase == 100000 && refunded.Line.RevenueShare == 70000 && refunded.Line.AfterRounding == 70000,
            "refunds preserve exact integer wages without epsilon ceiling");
        var held = Cash(150000); held.HoldScope = "order";
        Check(Calculate(original with { CashAllocations = [held] }).Run.GrossRevenue == 0, "held receipts recognize no revenue");
        var adjusted = original with { Orders = [new() { OrderId = "order", Subtotal = 160000, TotalAmount = 150000,
            MealCreditApplied = 10000, AdjustedAmount = 120000 }], CashAllocations = [Cash(60000)] };
        var reduced = Calculate(adjusted);
        Check(reduced.Line.RevenueBase == 60000 && reduced.Line.Sources.Single().AdjustedRevenueAmount == 120000,
            "charge reduction and half receipt are separate, auditable ratios");
        var partlyCredited = original with { Orders = [new() { OrderId = "order", Subtotal = 180000,
            TotalAmount = 170000, MealCreditApplied = 10000 }],
            Items = [Item("base", "nomination_base", 150000), Item("meal-a", "menu_item", 10000), Item("meal-b", "menu_set", 20000)],
            CashAllocations = [Cash(170000)] };
        Check(Calculate(partlyCredited).Run.MealRevenue == 20000 && Calculate(partlyCredited).Line.RevenueBase == 150000,
            "multiple meals conserve their credit including integer remainder");
        var returned = Item("meal", "menu_item", 5000); returned.OriginalLineTotal = 10000; returned.MealCreditAmount = 5000;
        var cancelledMeal = original with { Orders = [new() { OrderId = "order", Subtotal = 155000,
            TotalAmount = 150000, MealCreditApplied = 5000 }], Items = [Item("base", "nomination_base", 150000), returned] };
        Check(Calculate(cancelledMeal).Line.RevenueBase == 150000 && Calculate(cancelledMeal).Run.MealRevenue == 0,
            "partially cancelled meal uses only its remaining credit");
        var cancelledBase = Item("base", "nomination_base", 0); cancelledBase.OriginalLineTotal = 150000;
        var cancelled = Calculate(original with { Orders = [new() { OrderId = "order", Subtotal = 10000,
            TotalAmount = 0, MealCreditApplied = 10000 }], Items = [cancelledBase, Item("meal", "menu_item", 10000)], CashAllocations = [] });
        Check(cancelled.Line.RevenueBase == 0 && cancelled.Line.Sources.Single().CancelledAmount == 150000,
            "cancelled service earns nothing and retains the original cancellation in its source");
        var inconsistent = Item("meal", "menu_item", 10000); inconsistent.MealCreditAmount = 5000;
        Check(Calculate(original with { Items = [Item("base", "nomination_base", 150000), inconsistent] }).Anomalies
            .Any(x => x.Code == "MEAL_CREDIT_RECONCILIATION_REQUIRED" && x.Orders?.Single().OrderId == "order"),
            "inconsistent credit snapshots are blocked with a linked order");
        var tower = Item("tower", "menu_set", 100000); tower.IsChampagneTower = true; tower.RecipientStaffId = "staff";
        var champagne = Calculate(original with { Orders = [new() { OrderId = "order", Subtotal = 260000,
            TotalAmount = 250000, MealCreditApplied = 10000 }],
            Items = [Item("base", "nomination_base", 150000), Item("meal", "menu_item", 10000), tower], CashAllocations = [Cash(250000)] });
        Check(champagne.Line.RevenueBase == 150000 && champagne.Run.MealRevenue == 0 && champagne.Run.GrossRevenue == 250000,
            "adding champagne never changes nomination credit treatment");
        var withTip = original with { Orders = [new() { OrderId = "order", Subtotal = 180000, TotalAmount = 170000,
            MealCreditApplied = 10000, TipAmount = 20000 }],
            Tips = [new() { OrderId = "order", StaffId = "staff", TipAmount = 20000, StaffAmount = 20000 }], CashAllocations = [Cash(85000)] };
        var tip = Calculate(withTip);
        Check(tip.Line.RevenueBase == 75000 && tip.Line.DesignatedTip == 10000 && tip.Run.GrossRevenue == 75000,
            "tip is excluded from item revenue and never discounted by meal credit");
        var noCredit = Calculate(original with { Orders = [new() { OrderId = "order", Subtotal = 160000, TotalAmount = 160000 }], CashAllocations = [Cash(160000)] });
        Check(noCredit.Line.RevenueBase == 150000 && noCredit.Run.MealRevenue == 10000, "orders without credit retain original values");
        var activity = Calculate(original, "event");
        Check(activity.Run.GrossRevenue == 150000 && activity.Run.ActivityNetRevenue == 150000,
            "event revenue conserves the post-credit order total");
        var legacy = Calculate(original with { Cash = new(), CashAllocations = [] });
        Check(legacy.Line.RevenueBase == 150000 && legacy.Run.MealRevenue == 0, "legacy non-ledger calculation uses meal-only credit too");
        Console.WriteLine($"Meal credit allocation: {checks} checks passed.");

        (SettlementRunRow Run, SettlementResultLineRow Line, IReadOnlyList<SettlementAnomalyDto> Anomalies) Calculate(SettlementSourceData source, string day = "normal", bool support = false)
        {
            var method = typeof(SettlementService).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = method.Invoke(new SettlementService(null!, null!, null!, null!),
                [new SettlementRunRow { DayType = day, CompanyShareHours = 1, ActivityHoursConfirmed = true }, rule, source])!;
            var type = result.GetType();
            return ((SettlementRunRow)type.GetProperty("Run")!.GetValue(result)!,
                ((IReadOnlyList<SettlementResultLineRow>)type.GetProperty("Results")!.GetValue(result)!).Single(x => x.Role == (support ? "support_service_share" : "designated")),
                (IReadOnlyList<SettlementAnomalyDto>)type.GetProperty("Anomalies")!.GetValue(result)!);
        }
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    }

    private static SettlementOrderItemRevenueRow Item(string id, string kind, int amount) => new() {
        ItemId = id, OrderId = "order", ItemType = kind, ReferenceId = kind == "nomination_base" ? "staff" : null,
        OriginalLineTotal = amount, LineTotal = amount };
    private static SettlementCashAllocationRow Cash(int amount, string kind = "cash_receipt") => new() {
        Id = Guid.NewGuid().ToString("D"), OrderId = "order", Kind = kind, Amount = amount, SourceKind = "order",
        AllocationStatus = "confirmed", HoldScope = "none", CashPeriodId = "period" };
}
