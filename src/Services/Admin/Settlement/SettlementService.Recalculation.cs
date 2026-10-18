using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public sealed partial class SettlementService
{
    public Task<SettlementRecalculationPreviewDto> PreviewRecalculationAsync(SettlementCalculateRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        return _repository.RecalculateFinalizedAsync(request.BusinessDate, request.SessionNo, null,
            PrepareRecalculation, ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<SettlementOverviewDto> ConfirmRecalculationAsync(ConfirmSettlementRecalculationRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (!Guid.TryParseExact(request.OperationId, "D", out _) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500)
            throw new BusinessException("重新結算需要有效操作編號與 500 字以內的補正原因。", "SETTLEMENT_RECALCULATION_INVALID");
        await _repository.RecalculateFinalizedAsync(request.BusinessDate, request.SessionNo, request,
            PrepareRecalculation, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public Task<IReadOnlyList<SettlementRecalculationHistoryDto>> GetRecalculationHistoryAsync(
        DateOnly businessDate, int sessionNo, CancellationToken cancellationToken)
    {
        ValidateDate(businessDate);
        if (sessionNo < 1) throw new BusinessException("營業時段必須大於零。", "SETTLEMENT_SESSION_INVALID");
        return _repository.GetRecalculationHistoryAsync(businessDate, sessionNo, cancellationToken);
    }

    private SettlementRecalculationPlan PrepareRecalculation(SettlementRunRow run, SettlementSourceData source,
        IReadOnlyList<SettlementPaymentEventRow> payments, IReadOnlyList<SettlementCorrectionEventRow> corrections)
    {
        // Use the finalized rule, even when newer rules have since been published.
        SettlementRuleRow? rule = null;
        try { rule = JsonSerializer.Deserialize<SettlementRuleRow>(run.RuleSnapshotJson ?? "null"); }
        catch (JsonException) { }
        if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || rule.Id != run.RuleVersionId)
            throw new BusinessException("缺少有效的原結算規則快照，請先核對原始結算資料。", "SETTLEMENT_RULE_SNAPSHOT_REQUIRED");

        var refreshed = SettlementAttendanceSync.RefreshFinalizedSource(run.Id, source);
        var calculationRun = CopyRun(run);
        if (refreshed.Admissions.FirstOrDefault()?.UseAdmissionLedger != true && run.AdmissionFeeOverride is null)
            calculationRun.AdmissionFeeOverride = run.AdmissionFeeSnapshot;
        var calculation = Calculate(calculationRun, rule, refreshed);
        calculation.Run.AdmissionFeeOverride = run.AdmissionFeeOverride;
        var anomalies = calculation.Anomalies.ToList();
        if (source.Period.PeriodStatus is not ("closed" or "settled")
            || source.Period.UnfinishedOrderCount > 0 || source.Period.ActiveServiceCount > 0)
            anomalies.Add(new SettlementAnomalyDto("RECALCULATION_PERIOD_NOT_READY",
                "請先完成關店及未完成服務，再確認重新結算。"));
        if (source.AttendanceBackfillRequests.Any(x => x.Status == "pending"))
            anomalies.Add(new SettlementAnomalyDto("RECALCULATION_BACKFILL_PENDING",
                "仍有補打卡待核准，請完成核對後重新預覽。"));

        var keys = source.Results.Select(x => (x.StaffId, x.Role))
            .Concat(calculation.Results.Select(x => (x.StaffId, x.Role)))
            .Concat(source.StaffInputs.Select(x => (StaffId: (string?)x.StaffId, Role: x.Role)))
            .Concat(refreshed.StaffInputs.Select(x => (StaffId: (string?)x.StaffId, Role: x.Role)))
            .Distinct().OrderBy(x => x.StaffId, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal);
        var changes = new List<SettlementRecalculationChangeDto>();
        foreach (var key in keys)
        {
            var before = source.Results.SingleOrDefault(x => x.StaffId == key.StaffId && x.Role == key.Role);
            var after = calculation.Results.SingleOrDefault(x => x.StaffId == key.StaffId && x.Role == key.Role);
            var oldInput = source.StaffInputs.SingleOrDefault(x => x.StaffId == key.StaffId && x.Role == key.Role);
            var newInput = refreshed.StaffInputs.SingleOrDefault(x => x.StaffId == key.StaffId && x.Role == key.Role);
            if (oldInput?.ActualMinutes == newInput?.ActualMinutes
                && JsonSerializer.Serialize(before is null ? null : MapResult(before))
                    == JsonSerializer.Serialize(after is null ? null : MapResult(after))) continue;
            changes.Add(new SettlementRecalculationChangeDto(key.StaffId,
                after?.DisplayName ?? before?.DisplayName ?? newInput?.DisplayName ?? oldInput?.DisplayName ?? "公司",
                key.Role, oldInput?.ActualMinutes, newInput?.ActualMinutes,
                before?.PayableHours ?? 0, after?.PayableHours ?? 0,
                before?.AfterRounding ?? 0, after?.AfterRounding ?? 0,
                (decimal)(after?.AfterRounding ?? 0) - (before?.AfterRounding ?? 0)));
        }
        var activeCorrections = corrections.Where(x => x.Status != "cancelled").ToArray();
        var balances = calculation.Results.Where(x => x.StaffId is not null)
            .Select(x => x.StaffId!).Concat(source.Results.Where(x => x.StaffId is not null).Select(x => x.StaffId!))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .Select(staffId =>
            {
                var pay = calculation.Results.Where(x => x.StaffId == staffId).Sum(x => (decimal)x.AfterRounding);
                var paid = payments.Where(x => x.StaffId == staffId).Sum(x => x.EventKind == "payout" ? (decimal)x.Amount : -x.Amount);
                var manual = activeCorrections.Where(x => x.StaffId == staffId).Sum(x => (decimal)x.AmountDelta);
                var name = refreshed.PayrollAssignments.FirstOrDefault(x => x.StaffId == staffId)?.DisplayName
                    ?? source.Results.FirstOrDefault(x => x.StaffId == staffId)?.DisplayName ?? staffId;
                return new SettlementRecalculationBalanceDto(staffId, name, pay, paid, manual, pay + manual - paid);
            }).ToArray();
        var beforePayroll = source.Results.Sum(x => (decimal)x.AfterRounding);
        var afterPayroll = calculation.Results.Sum(x => (decimal)x.AfterRounding);
        var token = RecalculationToken(new { run, source, payments, corrections });
        var hasChanges = changes.Count > 0 || run.NetCash != calculation.Run.NetCash
            || run.CompanyIncome != calculation.Run.CompanyIncome || run.GrossRevenue != calculation.Run.GrossRevenue
            || run.RetainedAmount != calculation.Run.RetainedAmount
            || run.PendingFinanceCount != calculation.Run.PendingFinanceCount;
        var preview = new SettlementRecalculationPreviewDto(DateOnly.FromDateTime(run.BusinessDate),
            run.SessionNo, token, run.CorrectionVersion, hasChanges, beforePayroll, afterPayroll,
            afterPayroll - beforePayroll, run.NetCash, calculation.Run.NetCash,
            run.CompanyIncome, calculation.Run.CompanyIncome, changes, balances, anomalies,
            activeCorrections.Select(x => new SettlementRecalculationExistingCorrectionDto(x.StaffId, x.AmountDelta, x.Reason)).ToArray());
        return new SettlementRecalculationPlan(calculation.ToSave(rule, refreshed.StaffInputs), preview);
    }

    internal static string RecalculationToken(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(document.RootElement))));

        // SQL row order must not invalidate an otherwise identical preview.
        static string Canonical(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Canonical)
                .OrderBy(x => x, StringComparer.Ordinal)) + "]",
            _ => element.GetRawText()
        };
    }
}
