using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public static class OrderingTipAllocationCalculator
{
    public static IReadOnlyList<CalculatedOrderingFinanceTipAllocation> Calculate(long amount, string mode,
        IReadOnlyList<SaveOrderingFinanceTipAllocationRequest> allocations)
    {
        if (amount <= 0 || allocations.Count == 0 || allocations.Any(x => string.IsNullOrWhiteSpace(x.StaffId)) ||
            allocations.Select(x => x.StaffId).Distinct(StringComparer.Ordinal).Count() != allocations.Count)
            throw new BusinessException("請選擇金額有效且不重複的店員。", "FINANCE_TIP_ALLOCATION_INVALID");

        if (mode == "equal")
        {
            if (allocations.Any(x => x.Percentage is not null))
                throw new BusinessException("均分小費不需要填寫比例。", "FINANCE_TIP_ALLOCATION_INVALID");
            var ordered = allocations.OrderBy(x => x.StaffId, StringComparer.Ordinal).ToArray();
            var each = amount / ordered.Length;
            var remainder = amount % ordered.Length;
            return ordered.Select((x, index) => new CalculatedOrderingFinanceTipAllocation(x.StaffId, null,
                each + (index < remainder ? 1 : 0))).ToArray();
        }

        if (mode == "designated")
        {
            if (allocations.Count != 1 || allocations[0].Percentage is not null and not 100m)
                throw new BusinessException("指定一位店員時只能選擇一人。", "FINANCE_TIP_ALLOCATION_INVALID");
            return [new CalculatedOrderingFinanceTipAllocation(allocations[0].StaffId, 100m, amount)];
        }

        if (mode != "custom" || allocations.Any(x => x.Percentage is null or <= 0 or > 100 ||
                decimal.Round(x.Percentage.Value, 4) != x.Percentage.Value) ||
            allocations.Sum(x => x.Percentage!.Value) != 100m)
            throw new BusinessException("自訂比例必須大於 0，且合計為 100%。", "FINANCE_TIP_ALLOCATION_INVALID");

        var shares = allocations.Select(x =>
        {
            var exact = amount * x.Percentage!.Value / 100m;
            var whole = decimal.ToInt64(decimal.Floor(exact));
            return new { x.StaffId, Percentage = x.Percentage.Value, Amount = whole, Fraction = exact - whole };
        }).ToArray();
        var remaining = amount - shares.Sum(x => x.Amount);
        var remainderStaffIds = shares.OrderByDescending(x => x.Fraction)
            .ThenBy(x => x.StaffId, StringComparer.Ordinal).Take(checked((int)remaining))
            .Select(x => x.StaffId).ToHashSet(StringComparer.Ordinal);
        return shares.Select(x => new CalculatedOrderingFinanceTipAllocation(x.StaffId, x.Percentage,
            x.Amount + (remainderStaffIds.Contains(x.StaffId) ? 1 : 0))).ToArray();
    }
}
