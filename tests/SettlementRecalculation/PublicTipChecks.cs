using System.Reflection;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

static class PublicTipChecks
{
    public static void Run()
    {
        var count = 0;
        var service = new SettlementService(null!, null!, null!, null!);
        var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var validate = typeof(SettlementService).GetMethod("ValidatePublicTipInput", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rule = new SettlementRuleRow { TimeRoundMinutes = 30 };
        var run = new SettlementRunRow { CalculationVersion = 4, DayType = "normal" };
        var source = new SettlementSourceData
        {
            Orders = [new() { OrderId = "order", OrderNumber = "tip-order", TotalAmount = 1001, TipAmount = 1001 }],
            Tips = [new() { OrderId = "order", TipAmount = 1001, SourceType = "public_tip" }],
            StaffInputs = [new() { StaffId = "a", Role = "service", ActualMinutes = 180 },
                new() { StaffId = "b", Role = "designated", ActualMinutes = 60 },
                new() { StaffId = "c", Role = "backstage", ActualMinutes = 240, IsBackstageParticipant = true }],
            PayrollAssignments = [new() { StaffId = "a", PayrollRole = "service" }, new() { StaffId = "b", PayrollRole = "designated" },
                new() { StaffId = "c", PayrollRole = "backstage", CanUseBackstage = true }],
            Cash = new() { HasCashRecords = true, CashReceived = 1001, NetCash = 1001 },
            CashAllocations = [Cash("receipt", "cash_receipt", 1001)]
        };
        var full = Calculate(source);
        Check(full.Results.Single(x => x.StaffId == "a").PublicTip == 751 && full.Results.Single(x => x.StaffId == "b").PublicTip == 250,
            "1001 G follows 3:1 hours and deterministic integer remainder");
        Check(full.Results.Sum(x => x.PublicTip) == 1001 && full.Results.All(x => x.DesignatedTip == 0), "pool counted once, separate from designated tips");
        Check(full.Run.CompanyIncome == 0 && full.Run.CompanyRevenue == 0 && full.Run.GrossRevenue == 0,
            "public tips never enter company or revenue-share pools");
        Check(full.Results.Single(x => x.StaffId == "c").PublicTip == 0, "backstage attendance is excluded");
        Check(full.Results.Where(x => x.PublicTip > 0).All(x => x.Sources.Sum(s => s.Amount) == x.PublicTip
            && x.Sources.All(s => s.Kind == "public_tip" && s.Label.Contains("合計 4"))), "source snapshots preserve hours, order and exact allocated total");
        Check(source.Results.Count == 0 && source.StaffInputs[0].ActualMinutes == 180 && run.PublicTipAmount == 0,
            "preview does not mutate original inputs or finalized pool");
        var partial = Calculate(source with { CashAllocations = [Cash("partial", "cash_receipt", 501)] });
        Check(partial.Results.Sum(x => x.PublicTip) == 501 && partial.Run.PublicTipAmount == 501, "partial receipts recognize only paid tips");
        var refunded = Calculate(source with { CashAllocations = [Cash("receipt", "cash_receipt", 1001), Cash("refund", "cash_refund", 501)] });
        Check(refunded.Results.Sum(x => x.PublicTip) == 500, "refund reduces pool");
        var held = Cash("held", "cash_receipt", 1001); held.HoldScope = "order";
        Check(Calculate(source with { CashAllocations = [held] }).Results.Sum(x => x.PublicTip) == 0, "held cash excluded");
        var pending = Cash("pending", "cash_receipt", 1001); pending.AllocationStatus = "pending";
        Check(Calculate(source with { CashAllocations = [pending] }).Run.PublicTipAmount == 0, "pending cash excluded");
        Check(Calculate(source with { Cash = new(), CashAllocations = [] }).Run.PublicTipAmount == 0, "no ledger never invents historical receipts");
        var other = Cash("unrelated", "cash_receipt", 99999); other.OrderId = "other";
        Check(Calculate(source with { CashAllocations = source.CashAllocations.Append(other).ToArray() }).Run.PublicTipAmount == 1001,
            "unrelated order cash cannot change a tip entitlement");
        var reordered = Calculate(source with { StaffInputs = source.StaffInputs.Reverse().ToArray() });
        Check(reordered.Results.Single(x => x.StaffId == "a").PublicTip == 751, "SQL order cannot change remainder recipient");
        var zero = Calculate(source with { StaffInputs = [] });
        Check(zero.Anomalies.Any(x => x.Code == "PUBLIC_TIP_DENOMINATOR_ZERO"), "zero eligible hours blocks finalization");
        run.DayType = "event"; run.ActivityHoursConfirmed = true; run.CompanyShareHours = 0;
        var eventResult = Calculate(source);
        Check(eventResult.Results.Sum(x => x.PublicTip) == 1001 && eventResult.Results.Where(x => x.StaffId == "c").All(x => x.PublicTip == 0),
            "event day tips follow paid work hours even without activity-share input");
        run.DayType = "normal";
        run.CalculationVersion = 2; run.PublicTipAmount = 500;
        var legacy = Calculate(source);
        Check(legacy.Anomalies.Any(x => x.Code == "PUBLIC_TIP_LEGACY_RECONCILIATION_REQUIRED")
            && legacy.Run.CalculationVersion == 2 && legacy.Run.PublicTipAmount == 500,
            "an unresolved manual pool remains identifiable across repeated calculation saves");
        Reject(() => validate.Invoke(null, [run, 0, null]), "legacy manual pool requires a reconciliation reason");
        validate.Invoke(null, [run, 0, "mapped to source orders, duplicates removed"]);
        Reject(() => validate.Invoke(null, [run, 500, "reason"]), "new manual pool entry rejected");
        run.PublicTipAmount = 0;
        var oldEqual = Calculate(source with { Tips = [new() { OrderId = "order", StaffId = "a", StaffAmount = 1001, SourceType = "legacy_equal" }] });
        Check(oldEqual.Anomalies.Any(x => x.Code == "TIP_LEGACY_RECONCILIATION_REQUIRED"), "old per-head allocations require explicit order correction");
        // Several orders and a fraction of a G: integer payroll and all source rows must conserve the same pool.
        var multiple = source with {
            Orders = [.. source.Orders, new() { OrderId = "second", OrderNumber = "second-tip", TotalAmount = 3, TipAmount = 1 }],
            Tips = [.. source.Tips, new() { OrderId = "second", TipAmount = 1, SourceType = "public_tip" }],
            CashAllocations = [.. source.CashAllocations, new() { Id = "second-cash", OrderId = "second", SourceKind = "order", Kind = "cash_receipt",
                Amount = 2, AllocationStatus = "confirmed", HoldScope = "none", CashPeriodId = "period" }] };
        var multi = Calculate(multiple);
        Check(multi.Run.PublicTipAmount == 1001 && multi.Results.Sum(x => x.PublicTip) == 1001
            && multi.Results.Sum(x => x.Sources.Sum(s => s.Amount)) == 1001, "multiple source fractions never inflate the integer pool");
        var matrixMethod = typeof(SettlementService).GetMethod("PublicTipSourceShares", BindingFlags.NonPublic | BindingFlags.Static)!;
        var matrix = (Dictionary<(string StaffId, string OrderId), int>)matrixMethod.Invoke(null,
            [2, new Dictionary<string, decimal> { ["a"] = 1, ["b"] = 1 }, new SettlementPublicTipSourceDto[] {
                new("first", "first", 1, 1, 1, 1, "paid"), new("second", "second", 1, 1, 1, 1, "paid") }])!;
        Check(matrix.Where(x => x.Key.OrderId == "first").Sum(x => x.Value) == 1
            && matrix.Where(x => x.Key.OrderId == "second").Sum(x => x.Value) == 1
            && matrix.Where(x => x.Key.StaffId == "a").Sum(x => x.Value) == 1
            && matrix.Where(x => x.Key.StaffId == "b").Sum(x => x.Value) == 1,
            "rounding reconciles both individual wages and each source order; a source cannot be allocated twice");
        run = full.Run;
        rule.Id = "public-rule"; rule.DayType = "normal";
        run.RuleVersionId = rule.Id; run.RuleSnapshotJson = System.Text.Json.JsonSerializer.Serialize(rule);
        var prepare = typeof(SettlementService).GetMethod("PrepareRecalculation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var unchanged = (SettlementRecalculationPlan)prepare.Invoke(service,
            [run, source with { Results = full.Results, Period = new() { PeriodStatus = "settled" } },
                Array.Empty<SettlementPaymentEventRow>(), Array.Empty<SettlementCorrectionEventRow>(),
                new SettlementRecalculationAdjustmentsRequest { DayType = "normal" }])!;
        Check(!unchanged.Preview.HasChanges, "an unchanged derived pool is not treated as a manual zeroing adjustment");
        Console.WriteLine($"Passed {count} public-tip settlement checks.");

        Result Calculate(SettlementSourceData input)
        {
            var calculated = calculate.Invoke(service, [run, rule, input])!;
            var type = calculated.GetType();
            return new Result((SettlementRunRow)type.GetProperty("Run")!.GetValue(calculated)!,
                (IReadOnlyList<SettlementResultLineRow>)type.GetProperty("Results")!.GetValue(calculated)!,
                (IReadOnlyList<SettlementAnomalyDto>)type.GetProperty("Anomalies")!.GetValue(calculated)!);
        }
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        void Reject(Action action, string label) {
            try { action(); } catch (TargetInvocationException e) when (e.InnerException is BusinessException) { count++; return; }
            throw new Exception(label);
        }
    }
    private sealed record Result(SettlementRunRow Run, IReadOnlyList<SettlementResultLineRow> Results, IReadOnlyList<SettlementAnomalyDto> Anomalies);
    private static SettlementCashAllocationRow Cash(string id, string kind, int amount) => new() {
        Id = id, OrderId = "order", Kind = kind, Amount = amount, SourceKind = "order", CashPeriodId = "period",
        AllocationStatus = "confirmed", HoldScope = "none" };
}
