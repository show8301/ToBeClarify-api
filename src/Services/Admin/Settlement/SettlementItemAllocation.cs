using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;

namespace ToBeClarify.Api.Services.Admin.Settlement;

// Meal credit belongs only to eligible meals. Charge adjustments and receipts
// are separate order ratios, applied after each item's own credit and cancellation.
internal sealed class SettlementItemAllocation
{
    private readonly Dictionary<string, SettlementOrderRevenueRow> _orders;
    private readonly Dictionary<string, SettlementOrderItemRevenueRow> _items;
    private readonly Dictionary<string, decimal> _credits = new(StringComparer.Ordinal);
    private readonly SettlementRevenueAllocation _cash;

    public SettlementItemAllocation(SettlementSourceData source, SettlementRevenueAllocation cash,
        List<SettlementAnomalyDto> anomalies)
    {
        _orders = source.Orders.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        _items = source.Items.ToDictionary(x => x.ItemId, StringComparer.Ordinal);
        _cash = cash;
        foreach (var order in source.Orders)
        {
            var meals = source.Items.Where(x => x.OrderId == order.OrderId
                && (x.ItemType is "menu_item" or "menu_set") && !x.IsChampagneTower)
                .OrderBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
            var known = meals.Where(x => x.MealCreditAmount.HasValue).ToArray();
            var remaining = order.MealCreditApplied - known.Sum(x => x.MealCreditAmount!.Value);
            var missing = meals.Where(x => !x.MealCreditAmount.HasValue).ToArray();
            var total = missing.Sum(x => (decimal)x.LineTotal);
            var invalid = order.MealCreditApplied < 0 || remaining < 0 || remaining > total
                || known.Any(x => x.MealCreditAmount < 0 || x.MealCreditAmount > x.LineTotal);
            if (invalid)
                anomalies.Add(new("MEAL_CREDIT_RECONCILIATION_REQUIRED",
                    $"訂單 {order.OrderNumber} 餐飲折抵與有效餐飲品項不符，請核對取消與折抵紀錄。",
                    order.OrderId, Orders: [new(order.OrderId, order.OrderNumber)]));
            foreach (var item in known)
                _credits[item.ItemId] = Math.Clamp(item.MealCreditAmount!.Value, 0, Math.Max(0, item.LineTotal));
            // Legacy orders have no per-item credit snapshot. Cumulative integer
            // allocation preserves the exact order credit, including its final Gil.
            decimal cumulative = 0, allocated = 0;
            var credit = Math.Clamp(remaining, 0, Math.Max(0, total));
            foreach (var item in missing)
            {
                cumulative += item.LineTotal;
                var next = total <= 0 ? 0 : decimal.Floor(credit * cumulative / total);
                _credits[item.ItemId] = next - allocated;
                allocated = next;
            }
        }
    }

    public decimal Recognize(string orderId, decimal amount, string? itemId = null)
    {
        if (!_orders.TryGetValue(orderId, out var order)) return amount;
        var afterCredit = Math.Max(0, amount - (itemId is null ? 0 : _credits.GetValueOrDefault(itemId)));
        var originalRevenue = Math.Max(0, order.TotalAmount - order.TipAmount);
        var adjustedRevenue = Math.Max(0, (order.AdjustedAmount ?? order.TotalAmount) - order.TipAmount);
        // Multiply before dividing so exact monetary results do not acquire an
        // epsilon from an intermediate recurring ratio before wage ceiling.
        return originalRevenue <= 0 ? 0 : _cash.Recognize(orderId, afterCredit * adjustedRevenue) / originalRevenue;
    }

    public void Describe(SettlementResultSourceRow source)
    {
        if (source.OrderItemId is null || !_items.TryGetValue(source.OrderItemId, out var item)
            || !_orders.TryGetValue(source.OrderId, out var order)) return;
        source.CancelledAmount = Math.Max(0, (item.OriginalLineTotal ?? item.LineTotal) - item.LineTotal);
        source.MealCreditAmount = _credits.GetValueOrDefault(item.ItemId);
        source.AfterCreditAmount = Math.Max(0, item.LineTotal - source.MealCreditAmount.Value);
        source.OrderRevenueAmount = Math.Max(0, order.TotalAmount - order.TipAmount);
        source.AdjustedRevenueAmount = Math.Max(0, (order.AdjustedAmount ?? order.TotalAmount) - order.TipAmount);
    }
}
