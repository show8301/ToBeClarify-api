using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public static class OrderTipDistributionPolicy
{
    public static OrderTipDistributionSnapshot Calculate(int amount, string mode,
        IReadOnlyList<SaveOrderingFinanceTipAllocationRequest> allocations,
        IReadOnlyList<OrderingFinanceTipStaffOptionDto> eligible)
    {
        if (amount is < 1 or > 1000000 || allocations is null || allocations.Count > 200 || allocations.Any(x => x is null || string.IsNullOrWhiteSpace(x.StaffId)))
            throw new BusinessException("小費分配資料無效。", "TIP_DISTRIBUTION_INVALID");
        var staff = eligible.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        if (allocations.Any(x => !staff.ContainsKey(x.StaffId)) ||
            mode == "equal" && !staff.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(allocations.Select(x => x.StaffId)))
            throw new BusinessException("出勤名單已變更，請重新讀取並確認小費分配。", "TIP_ATTENDANCE_CHANGED");
        var shares = OrderingTipAllocationCalculator.Calculate(amount, mode, allocations);
        return new(mode, shares.OrderBy(x => x.StaffId, StringComparer.Ordinal)
            .Select(x => new OrderingFinanceTipAllocationDto(x.StaffId, staff[x.StaffId].DisplayName, x.Percentage, x.Amount)).ToArray());
    }
}
