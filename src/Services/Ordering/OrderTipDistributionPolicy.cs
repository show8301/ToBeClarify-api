using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public static class OrderTipDistributionPolicy
{
    public static OrderTipDistributionSnapshot FromStandard(int amount, string? staffId, string? staffName, int percentage)
    {
        var staffAmount = string.IsNullOrWhiteSpace(staffId) ? 0 : (int)((long)amount * percentage / 100);
        return staffAmount == 0 ? new("public", [], amount)
            : new("custom", [new(staffId!, staffName!, percentage, staffAmount)], amount - staffAmount);
    }

    public static OrderTipDistributionSnapshot Calculate(int amount, string mode,
        IReadOnlyList<SaveOrderingFinanceTipAllocationRequest> allocations,
        IReadOnlyList<OrderingFinanceTipStaffOptionDto> eligible)
    {
        if (amount is < 1 or > 999999999 || allocations is null || allocations.Count > 200 || allocations.Any(x => x is null || string.IsNullOrWhiteSpace(x.StaffId)))
            throw new BusinessException("小費分配資料無效。", "TIP_DISTRIBUTION_INVALID");
        if (mode == "public")
        {
            if (allocations.Count != 0) throw new BusinessException("公共小費於結算時按工時分配，不指定人員。", "TIP_DISTRIBUTION_INVALID");
            return new("public", [], amount);
        }
        if (mode == "equal") throw new BusinessException("請改用公共小費（按工時分配）。", "TIP_EQUAL_RETIRED");
        var staff = eligible.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        if (allocations.Any(x => !staff.ContainsKey(x.StaffId)))
            throw new BusinessException("出勤名單已變更，請重新讀取並確認小費分配。", "TIP_ATTENDANCE_CHANGED");
        var shares = OrderingTipAllocationCalculator.Calculate(amount, mode, allocations);
        return new(mode, shares.OrderBy(x => x.StaffId, StringComparer.Ordinal)
            .Select(x => new OrderingFinanceTipAllocationDto(x.StaffId, staff[x.StaffId].DisplayName, x.Percentage, x.Amount)).ToArray());
    }
}
