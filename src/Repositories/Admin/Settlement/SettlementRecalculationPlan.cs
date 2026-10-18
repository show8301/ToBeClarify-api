using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed record SettlementRecalculationPlan(SaveCalculationData Calculation,
    SettlementRecalculationPreviewDto Preview)
{
    public void ValidateConfirmation(ConfirmSettlementRecalculationRequest confirmation)
    {
        if (Preview.CorrectionVersion != confirmation.ExpectedCorrectionVersion
            || !string.Equals(Preview.SourceToken, confirmation.SourceToken, StringComparison.Ordinal))
            throw new ConflictException("出勤、金額、支付或結算版本已更新，請重新預覽並核對差額。", "SETTLEMENT_RECALCULATION_CHANGED");
        if (Preview.Anomalies.Count > 0)
            throw new BusinessException("仍有結算異常，請完成補正後重新預覽。", "SETTLEMENT_REQUIRES_MANUAL_HANDLING");
        if (!Preview.HasChanges)
            throw new ConflictException("目前資料與正式結算相同，無需建立新的結算版本。", "SETTLEMENT_RECALCULATION_UNCHANGED");
    }
}
