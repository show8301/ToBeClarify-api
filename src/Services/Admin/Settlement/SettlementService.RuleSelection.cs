using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public sealed partial class SettlementService
{
    private async Task<SettlementRuleRow?> GetSelectedRuleAsync(string? ruleId, string? dayType, CancellationToken ct)
    {
        if (ruleId is null) return null;
        if (string.IsNullOrWhiteSpace(ruleId) || ruleId.Length > 36)
            throw new BusinessException("規則版本無效，請重新選擇。", "SETTLEMENT_RULE_INVALID");
        var rule = (await _repository.GetRulesAsync(null, ct)).SingleOrDefault(x => x.Id == ruleId)
            ?? throw new BusinessException("找不到指定規則版本，請重新載入。", "SETTLEMENT_RULE_NOT_FOUND");
        ValidateSelectedRule(rule, dayType);
        return rule;
    }

    private static void ValidateSelectedRule(SettlementRuleRow rule, string? dayType)
    {
        if (dayType is not null && rule.DayType != dayType)
            throw new BusinessException("規則版本與日期類型不符，請選擇相同類型的規則。", "SETTLEMENT_RULE_DAY_TYPE_MISMATCH");
    }
}
