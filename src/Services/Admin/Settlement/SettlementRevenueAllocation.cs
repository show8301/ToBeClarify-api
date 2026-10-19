using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;

namespace ToBeClarify.Api.Services.Admin.Settlement;

// Cash is matched to its own order. Item-tagged payments belong to that order's
// payment balance; partial order payments are apportioned over its charge lines.
internal sealed class SettlementRevenueAllocation
{
    private readonly Dictionary<string, (decimal Cash, decimal Payable)> _balances = new(StringComparer.Ordinal);
    public bool UsesLedger { get; }
    public decimal AdmissionRevenue { get; }

    public SettlementRevenueAllocation(SettlementSourceData source, List<SettlementAnomalyDto> anomalies)
    {
        UsesLedger = source.Cash.HasCashRecords || source.Admissions.Any(x => x.UseAdmissionLedger);
        if (!UsesLedger) return;

        var orders = source.Orders.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        var items = source.Items.DistinctBy(x => x.ItemId).ToDictionary(x => x.ItemId, StringComparer.Ordinal);
        var cashByOrder = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var chargeByOrder = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var admissionCharges = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var admissionCash = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var record in source.CashAllocations)
        {
            if (record.Amount == 0) continue;
            if (record.SourceKind == "admission" && record.Kind is "charge_add" or "charge_reduce")
            {
                admissionCharges[record.SessionId] = admissionCharges.GetValueOrDefault(record.SessionId)
                    + (record.Kind == "charge_add" ? record.Amount : -record.Amount);
                continue;
            }
            if (record.AllocationStatus != "confirmed" || record.HoldScope != "none") continue;
            var isCash = record.Kind is "cash_receipt" or "cash_refund";
            if (isCash && record.CashPeriodId is null) continue;
            var amount = record.Kind is "cash_receipt" or "charge_add" ? record.Amount : -record.Amount;
            if (record.SourceKind == "admission" && isCash)
            {
                admissionCash[record.SessionId] = admissionCash.GetValueOrDefault(record.SessionId) + amount;
                continue;
            }
            var orderId = record.OrderId;
            if (record.SourceKind == "item")
            {
                if (record.OrderItemId is null || !items.TryGetValue(record.OrderItemId, out var item)
                    || orderId is not null && orderId != item.OrderId)
                {
                    anomalies.Add(new("FINANCE_ITEM_ALLOCATION_INVALID", "分項收退款找不到一致的訂單品項，請核對款項歸屬。", record.Id));
                    continue;
                }
                orderId = item.OrderId;
            }
            if (record.SourceKind is not ("order" or "item") || orderId is null || !orders.ContainsKey(orderId))
            {
                anomalies.Add(new(isCash ? "FINANCE_CASH_ALLOCATION_REQUIRED" : "FINANCE_CHARGE_ALLOCATION_REQUIRED",
                    "收退款或加收折讓尚未對應有效訂單或入場帳，未自動攤入全店分潤。", record.Id));
                continue;
            }
            if (isCash) cashByOrder[orderId] = cashByOrder.GetValueOrDefault(orderId) + amount;
            else chargeByOrder[orderId] = chargeByOrder.GetValueOrDefault(orderId) + amount;
        }
        foreach (var order in source.Orders)
        {
            var payable = Math.Max(0, order.AdjustedAmount ?? order.TotalAmount);
            var chargeDelta = chargeByOrder.GetValueOrDefault(order.OrderId);
            if (chargeDelta != 0 && order.AdjustedAmount != Math.Max(0, order.TotalAmount + chargeDelta))
                anomalies.Add(new("ORDER_CHARGE_RECONCILIATION_REQUIRED",
                    $"訂單 {order.OrderNumber} 另有加收或折讓 {chargeDelta:N0} G，請確認對應的結算金額調整。",
                    order.OrderId, Orders: [new(order.OrderId, order.OrderNumber)]));
            var cash = cashByOrder.GetValueOrDefault(order.OrderId);
            if (cash < 0 || cash > payable)
                anomalies.Add(new("ORDER_CASH_RECONCILIATION_REQUIRED",
                    $"訂單 {order.OrderNumber} 可分配淨收款 {cash:N0} G 與應收 {payable:N0} G 不符，請核對退款或保留款。",
                    order.OrderId, Orders: [new(order.OrderId, order.OrderNumber)]));
            _balances[order.OrderId] = (Math.Clamp(cash, 0, payable), payable);
        }
        foreach (var sessionId in admissionCharges.Keys.Concat(admissionCash.Keys).Distinct(StringComparer.Ordinal))
        {
            var payable = Math.Max(0, admissionCharges.GetValueOrDefault(sessionId));
            var cash = admissionCash.GetValueOrDefault(sessionId);
            if (cash < 0 || cash > payable)
                anomalies.Add(new("ADMISSION_CASH_RECONCILIATION_REQUIRED",
                    "入場淨收款與該客人的入場應收不符，請核對退款或保留款；差額未攤入其他來源。", sessionId));
            AdmissionRevenue += Math.Clamp(cash, 0, payable);
        }
    }

    public decimal Recognize(string orderId, decimal amount)
    {
        if (!UsesLedger) return amount;
        var balance = _balances.GetValueOrDefault(orderId);
        // Multiply before dividing: rounding a repeating ratio first can turn
        // an exact integer wage into integer + epsilon, then ceil adds 1 G.
        return balance.Payable <= 0 ? 0 : amount * balance.Cash / balance.Payable;
    }
}
