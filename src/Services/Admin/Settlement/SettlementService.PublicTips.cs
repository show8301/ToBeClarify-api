using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public sealed partial class SettlementService
{
    private static void ValidatePublicTipInput(SettlementRunRow run, int requested, string? reconciliationReason)
    {
        if (requested != 0)
            throw new BusinessException("公共小費改由訂單及有效實收彙總，請勿手動輸入。", "PUBLIC_TIP_USE_ORDER");
        if (run.CalculationVersion < 4 && run.PublicTipAmount > 0 && string.IsNullOrWhiteSpace(reconciliationReason))
            throw new BusinessException("請先核對舊手動公共小費與來源訂單，填寫核對原因後改用訂單彙總。", "PUBLIC_TIP_LEGACY_RECONCILIATION_REQUIRED");
    }

    private static SettlementPublicTipPoolDto PublicTipPool(SettlementSourceData source)
    {
        var allocation = new SettlementRevenueAllocation(source, []);
        var orders = source.Orders.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        var rows = source.Tips.Where(x => x.SourceType == "public_tip")
            .GroupBy(x => x.OrderId).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var order = orders.GetValueOrDefault(group.Key);
                var original = group.Sum(x => (decimal)x.TipAmount);
                var recognized = allocation.UsesLedger ? allocation.Recognize(group.Key, original) : 0;
                var payable = Math.Max(0, order?.AdjustedAmount ?? order?.TotalAmount ?? 0);
                var collected = allocation.UsesLedger ? allocation.Recognize(group.Key, payable) : 0;
                return new SettlementPublicTipSourceDto(group.Key, order?.OrderNumber ?? group.Key,
                    original, payable, collected, recognized,
                    recognized == 0 ? "未有可分配的有效實收" : recognized < original ? "部分實收／退款／保留，按比例認列" : "全額認列");
            }).ToArray();
        var total = rows.Sum(x => x.RecognizedAmount);
        var legacyStore = source.Tips.Where(x => x.StoreAmount > 0).GroupBy(x => x.OrderId)
            .OrderBy(x => x.Key, StringComparer.Ordinal).Select(group =>
            {
                var order = orders.GetValueOrDefault(group.Key);
                var original = group.Sum(x => (decimal)x.StoreAmount);
                var payable = Math.Max(0, order?.AdjustedAmount ?? order?.TotalAmount ?? 0);
                return new SettlementPublicTipSourceDto(group.Key, order?.OrderNumber ?? group.Key, original,
                    payable, allocation.UsesLedger ? allocation.Recognize(group.Key, payable) : 0,
                    allocation.UsesLedger ? allocation.Recognize(group.Key, original) : 0,
                    "舊店家分配，尚未轉為公共小費");
            }).ToArray();
        return new(total, decimal.Floor(total), rows, legacyStore);
    }

    // Largest remainders, with ordinal keys as a stable tie breaker. Never create extra tips by ceiling each wage.
    private static Dictionary<string, int> SplitPublicTips(int amount, IReadOnlyDictionary<string, decimal> weights)
    {
        var result = weights.ToDictionary(x => x.Key, _ => 0, StringComparer.Ordinal);
        var denominator = weights.Values.Sum();
        if (amount <= 0 || denominator <= 0) return result;
        var shares = weights.Select(x => new { x.Key, Exact = amount * x.Value / denominator }).ToArray();
        foreach (var share in shares) result[share.Key] = (int)decimal.Floor(share.Exact);
        var remaining = amount - result.Values.Sum();
        foreach (var share in shares.OrderByDescending(x => x.Exact - decimal.Floor(x.Exact))
            .ThenBy(x => x.Key, StringComparer.Ordinal).Take(remaining)) result[share.Key]++;
        return result;
    }

    private static Dictionary<(string StaffId, string OrderId), int> PublicTipSourceShares(int total,
        IReadOnlyDictionary<string, decimal> hours, IReadOnlyList<SettlementPublicTipSourceDto> sources)
    {
        var cells = new Dictionary<(string StaffId, string OrderId), int>();
        var denominator = hours.Values.Sum();
        if (denominator <= 0) return cells;
        var staffRemaining = SplitPublicTips(total, hours);
        var orderRemaining = SplitPublicTips(total, sources.ToDictionary(x => x.OrderId, x => x.RecognizedAmount));
        var remainders = new List<(string StaffId, string OrderId, decimal Fraction)>();
        foreach (var order in orderRemaining.ToArray())
        foreach (var staff in hours)
        {
            var exact = order.Value * staff.Value / denominator;
            var floor = (int)decimal.Floor(exact);
            cells[(staff.Key, order.Key)] = floor;
            staffRemaining[staff.Key] -= floor;
            orderRemaining[order.Key] -= floor;
            remainders.Add((staff.Key, order.Key, exact - floor));
        }
        foreach (var cell in remainders.OrderByDescending(x => x.Fraction)
            .ThenBy(x => x.StaffId, StringComparer.Ordinal).ThenBy(x => x.OrderId, StringComparer.Ordinal))
        {
            if (staffRemaining[cell.StaffId] == 0 || orderRemaining[cell.OrderId] == 0) continue;
            cells[(cell.StaffId, cell.OrderId)]++;
            staffRemaining[cell.StaffId]--;
            orderRemaining[cell.OrderId]--;
        }
        // Balance any residual matrix margins in stable order. Both personal and order totals must reconcile.
        foreach (var staff in staffRemaining.Keys.Order(StringComparer.Ordinal))
        foreach (var order in orderRemaining.Keys.Order(StringComparer.Ordinal))
        {
            var amount = Math.Min(staffRemaining[staff], orderRemaining[order]);
            cells[(staff, order)] += amount;
            staffRemaining[staff] -= amount;
            orderRemaining[order] -= amount;
        }
        return cells;
    }

    private static void AddPublicTipReconciliationAnomalies(SettlementRunRow originalRun, SettlementSourceData source,
        List<SettlementAnomalyDto> anomalies)
    {
        if (originalRun.CalculationVersion < 4 && originalRun.PublicTipAmount > 0)
            anomalies.Add(new("PUBLIC_TIP_LEGACY_RECONCILIATION_REQUIRED", "舊手動公共小費尚未核對來源，請填寫核對原因後改用訂單彙總。"));
        foreach (var tip in source.Tips.Where(x => x.SourceType == "legacy_equal" || x.SourceType == "tip_supplement")
            .DistinctBy(x => x.OrderId))
            anomalies.Add(new("TIP_LEGACY_RECONCILIATION_REQUIRED", "此訂單仍有舊版人頭均分或補登小費；請核對並更正原訂單分配，作廢重複補登後再結算。",
                tip.OrderId, Orders: [new(tip.OrderId, source.Orders.FirstOrDefault(x => x.OrderId == tip.OrderId)?.OrderNumber ?? tip.OrderId)]));
    }

    private static void ApplyPublicTips(SettlementRunRow originalRun, SettlementRunRow calculatedRun,
        SettlementRuleRow rule, SettlementSourceData source, List<SettlementResultLineRow> results,
        List<SettlementAnomalyDto> anomalies)
    {
        var pool = PublicTipPool(source);
        AddPublicTipReconciliationAnomalies(originalRun, source, anomalies);
        if (pool.DistributableAmount > int.MaxValue)
            throw new BusinessException("公共小費總額超過可處理範圍，請核對來源訂單。", "PUBLIC_TIP_AMOUNT_OUT_OF_RANGE");
        var poolAmount = (int)pool.DistributableAmount;
        var unresolvedManual = originalRun.CalculationVersion < 4 && originalRun.PublicTipAmount > 0;
        calculatedRun.PublicTipAmount = unresolvedManual ? originalRun.PublicTipAmount : poolAmount;
        if (unresolvedManual) calculatedRun.CalculationVersion = originalRun.CalculationVersion;
        var assignments = source.PayrollAssignments.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        var eligible = source.StaffInputs.Where(x => assignments.TryGetValue(x.StaffId, out var a) && a.PayrollRole == x.Role
                && IsPublicTipEligible(x, PayableHours(x.ActualMinutes, rule.TimeRoundMinutes)))
            .GroupBy(x => x.StaffId, StringComparer.Ordinal).ToDictionary(x => x.Key,
                x => PayableHours(x.Single().ActualMinutes, rule.TimeRoundMinutes), StringComparer.Ordinal);
        var hours = eligible.Values.Sum();
        if (pool.RecognizedAmount > 0 && hours == 0)
            anomalies.Add(new("PUBLIC_TIP_DENOMINATOR_ZERO", "有公共小費，但合資格計薪總工時為 0，請先補正出勤與計薪職位。"));
        var shares = SplitPublicTips(poolAmount, eligible);
        var sourceShares = PublicTipSourceShares(poolAmount, eligible, pool.Sources);
        foreach (var pair in shares)
        {
            var role = assignments[pair.Key].PayrollRole!;
            var result = results.SingleOrDefault(x => x.StaffId == pair.Key && x.Role == role);
            if (result is null && pair.Value == 0) continue;
            if (result is null)
            {
                // Event-day activity hours need not exist to receive a worked-hours tip.
                result = MakeResult(calculatedRun, pair.Key, role, eligible[pair.Key], 0, 0, 0, 0, 0, 0, 1,
                    source.StaffInputs.First(x => x.StaffId == pair.Key).DisplayName, "normal", null);
                results.Add(result);
            }
            result.PublicTip = pair.Value;
            result.BeforeRounding = result.PreferredPay + result.DesignatedTip + result.PublicTip;
            result.AfterRounding = checked((int)decimal.Ceiling(result.BeforeRounding));
            result.CompanySubsidy = result.AfterRounding - result.BeforeRounding;
            result.Sources = result.Sources.Concat(pool.Sources.Select(tip => new SettlementResultSourceRow
            {
                Kind = "public_tip", OrderId = tip.OrderId, OrderNumber = tip.OrderNumber,
                Label = FormattableString.Invariant($"公共小費：本人 {eligible[pair.Key]}／合計 {hours} 小時"),
                OriginalAmount = tip.OriginalAmount, BasisAmount = tip.RecognizedAmount,
                OrderPayableAmount = tip.OrderPayableAmount, OrderCollectedAmount = tip.OrderCollectedAmount,
                Percentage = hours <= 0 ? 0 : eligible[pair.Key] * 100m / hours,
                Amount = sourceShares[(pair.Key, tip.OrderId)]
            })).ToArray();
        }
    }
}
