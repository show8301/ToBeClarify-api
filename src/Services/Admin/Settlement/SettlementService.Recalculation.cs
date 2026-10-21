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
    public async Task<SettlementRecalculationPreviewDto> PreviewRecalculationAsync(PreviewSettlementRecalculationRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        var selected = await GetSelectedRuleAsync(request.Adjustments?.RuleVersionId, request.Adjustments?.DayType, cancellationToken);
        return await _repository.RecalculateFinalizedAsync(request.BusinessDate, request.SessionNo, null,
            (run, source, payments, corrections) => PrepareRecalculation(run, source with { SelectedRule = selected }, payments, corrections, request.Adjustments),
            ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<SettlementOverviewDto> ConfirmRecalculationAsync(ConfirmSettlementRecalculationRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (!Guid.TryParseExact(request.OperationId, "D", out _) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500)
            throw new BusinessException("重新結算需要有效操作編號與 500 字以內的補正原因。", "SETTLEMENT_RECALCULATION_INVALID");
        var selected = await GetSelectedRuleAsync(request.Adjustments?.RuleVersionId, request.Adjustments?.DayType, cancellationToken);
        await _repository.RecalculateFinalizedAsync(request.BusinessDate, request.SessionNo, request,
            (run, source, payments, corrections) => PrepareRecalculation(run, source with { SelectedRule = selected }, payments, corrections, request.Adjustments),
            ActorId(actor), _clock.LocalDateTime, cancellationToken);
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
        IReadOnlyList<SettlementPaymentEventRow> payments, IReadOnlyList<SettlementCorrectionEventRow> corrections,
        SettlementRecalculationAdjustmentsRequest? adjustments)
    {
        // Keep the finalized snapshot unless an explicitly selected persisted rule is supplied.
        SettlementRuleRow? rule = null;
        try { rule = JsonSerializer.Deserialize<SettlementRuleRow>(run.RuleSnapshotJson ?? "null"); }
        catch (JsonException) { }
        if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || rule.Id != run.RuleVersionId)
            throw new BusinessException("缺少有效的原結算規則快照，請先核對原始結算資料。", "SETTLEMENT_RULE_SNAPSHOT_REQUIRED");

        var previousRule = rule;
        if (adjustments?.RuleVersionId is not null)
        {
            if (source.SelectedRule?.Id != adjustments.RuleVersionId)
                throw new BusinessException("指定規則未通過版本核對，請重新預覽。", "SETTLEMENT_RULE_INVALID");
            rule = source.SelectedRule;
        }
        ValidateSelectedRule(rule, adjustments?.DayType ?? run.DayType);
        var calculationRun = CopyRun(run);
        var refreshed = SettlementAttendanceSync.RefreshFinalizedSource(run.Id, source);
        if (refreshed.Admissions.FirstOrDefault()?.UseAdmissionLedger != true && calculationRun.AdmissionFeeOverride is null)
            calculationRun.AdmissionFeeOverride = run.AdmissionFeeSnapshot;
        var changedAdjustmentInputs = false;
        if (adjustments is not null)
        {
            ValidateRecalculationAdjustments(adjustments, refreshed);
            calculationRun.DayType = adjustments.DayType;
            ValidatePublicTipInput(run, adjustments.PublicTipAmount, adjustments.PublicTipReconciliationReason);
            calculationRun.PublicTipAmount = 0;
            calculationRun.AdmissionFeeOverride = adjustments.AdmissionFeeOverride;
            calculationRun.ActivityExpense = adjustments.ActivityExpense;
            calculationRun.CompanyShareHours = adjustments.CompanyShareHours;
            calculationRun.ActivityHoursConfirmed = adjustments.ActivityHoursConfirmed;
            var proposedInputs = ApplyRecalculationStaffInputs(run.Id, adjustments.StaffInputs,
                adjustments.RemovedStaffInputs, refreshed);
            changedAdjustmentInputs = !RecalculationInputsEqual(refreshed.StaffInputs, proposedInputs)
                || run.DayType != calculationRun.DayType
                || run.AdmissionFeeOverride != calculationRun.AdmissionFeeOverride
                || run.ActivityExpense != calculationRun.ActivityExpense
                || run.CompanyShareHours != calculationRun.CompanyShareHours
                || run.ActivityHoursConfirmed != calculationRun.ActivityHoursConfirmed;
            refreshed = refreshed with { StaffInputs = proposedInputs };
        }
        var calculation = Calculate(calculationRun, rule, refreshed);
        calculation.Run.AdmissionFeeOverride = adjustments is null
            ? run.AdmissionFeeOverride
            : calculationRun.AdmissionFeeOverride;
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
                && oldInput?.ActivityHours == newInput?.ActivityHours
                && oldInput?.IsBackstageParticipant == newInput?.IsBackstageParticipant
                && JsonSerializer.Serialize(before is null ? null : MapResult(before))
                    == JsonSerializer.Serialize(after is null ? null : MapResult(after))) continue;
            changes.Add(new SettlementRecalculationChangeDto(key.StaffId,
                after?.DisplayName ?? before?.DisplayName ?? newInput?.DisplayName ?? oldInput?.DisplayName ?? "公司",
                key.Role, oldInput?.ActualMinutes, newInput?.ActualMinutes,
                before?.PayableHours ?? 0, after?.PayableHours ?? 0,
                before?.AfterRounding ?? 0, after?.AfterRounding ?? 0,
                (decimal)(after?.AfterRounding ?? 0) - (before?.AfterRounding ?? 0),
                oldInput?.ActivityHours, newInput?.ActivityHours));
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
        var token = RecalculationToken(new { run, source, payments, corrections, adjustments });
        var roleInputsChanged = !source.StaffInputs.OrderBy(x => x.Id).Select(x => (x.Id, x.Role, x.PayrollSnapshotVersion))
            .SequenceEqual(refreshed.StaffInputs.OrderBy(x => x.Id).Select(x => (x.Id, x.Role, x.PayrollSnapshotVersion)));
        var hasChanges = changes.Count > 0 || roleInputsChanged || run.NetCash != calculation.Run.NetCash
            || run.CompanyIncome != calculation.Run.CompanyIncome || run.GrossRevenue != calculation.Run.GrossRevenue
            || run.RetainedAmount != calculation.Run.RetainedAmount
            || run.PendingFinanceCount != calculation.Run.PendingFinanceCount || changedAdjustmentInputs || rule.Id != run.RuleVersionId
            || run.CalculationVersion != calculation.Run.CalculationVersion;
        var preview = new SettlementRecalculationPreviewDto(DateOnly.FromDateTime(run.BusinessDate),
            run.SessionNo, token, run.CorrectionVersion, hasChanges, beforePayroll, afterPayroll,
            afterPayroll - beforePayroll, run.NetCash, calculation.Run.NetCash,
            run.CompanyIncome, calculation.Run.CompanyIncome, changes, balances,
            anomalies.Select(item => EnrichAnomaly(source, item)).ToArray(),
            activeCorrections.Select(x => new SettlementRecalculationExistingCorrectionDto(x.StaffId, x.AmountDelta, x.Reason)).ToArray(),
            previousRule.Id, rule.Id, DateOnly.FromDateTime(previousRule.EffectiveFrom), DateOnly.FromDateTime(rule.EffectiveFrom));
        return new SettlementRecalculationPlan(calculation.ToSave(rule, refreshed.StaffInputs), preview);
    }

    private static void ValidateRecalculationAdjustments(SettlementRecalculationAdjustmentsRequest adjustments,
        SettlementSourceData source)
    {
        if (adjustments.DayType is not ("normal" or "event") || adjustments.PublicTipAmount < 0
            || adjustments.ActivityExpense < 0 || adjustments.AdmissionFeeOverride < 0
            || adjustments.CompanyShareHours is < 0 or > 100000)
            throw new BusinessException("重新結算輸入超出允許範圍。", "SETTLEMENT_RECALCULATION_INPUT_INVALID");
        if (adjustments.StaffInputs.GroupBy(x => (x.StaffId, x.Role)).Any(x => x.Count() > 1))
            throw new BusinessException("同一人同一角色只能有一筆計薪輸入。", "SETTLEMENT_INPUT_DUPLICATED");
        if (adjustments.RemovedStaffInputs.GroupBy(x => (x.StaffId, x.Role)).Any(x => x.Count() > 1)
            || adjustments.StaffInputs.Select(x => (x.StaffId, x.Role))
                .Intersect(adjustments.RemovedStaffInputs.Select(x => (x.StaffId, x.Role))).Any())
            throw new BusinessException("同一筆計薪輸入不可同時修改與移除。", "SETTLEMENT_INPUT_DUPLICATED");
        var assignments = source.PayrollAssignments.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        foreach (var input in adjustments.StaffInputs)
        {
            if (!assignments.TryGetValue(input.StaffId, out var assignment)
                || (input.Role != assignment.PayrollRole && !(input.Role == "backstage" && assignment.CanUseBackstage))
                || input.PayrollSnapshotVersion != assignment.PayrollSnapshotVersion)
                throw new BusinessException("重新結算人員或職位與上班快照不符，請重新載入後核對。", "SETTLEMENT_PAYROLL_SNAPSHOT_INVALID");
        }
    }

    private static List<SettlementStaffInputRow> ApplyRecalculationStaffInputs(string settlementId,
        IReadOnlyList<SettlementRecalculationStaffInputAdjustmentRequest> proposed,
        IReadOnlyList<SettlementRecalculationStaffInputKeyRequest> removed, SettlementSourceData refreshed)
    {
        var current = refreshed.StaffInputs.ToDictionary(x => (x.StaffId, x.Role));
        var assignments = refreshed.PayrollAssignments.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        foreach (var key in removed)
        {
            if (!current.Remove((key.StaffId, key.Role)))
                throw new BusinessException("要移除的計薪輸入已更新，請重新讀取結算資料。", "SETTLEMENT_RECALCULATION_INPUT_CHANGED");
        }
        foreach (var input in proposed)
        {
            var exists = current.TryGetValue((input.StaffId, input.Role), out var existing);
            var assignment = assignments[input.StaffId];
            if (!exists && (!input.ActualMinutesChanged || !input.ActivityHoursChanged || !input.BackstageParticipantChanged))
                throw new BusinessException("新增計薪輸入缺少完整調整值，請重新載入後再試。", "SETTLEMENT_RECALCULATION_INPUT_INVALID");
            var row = exists ? new SettlementStaffInputRow
            {
                Id = existing!.Id, SettlementId = existing.SettlementId, StaffId = existing.StaffId,
                CreatedAt = existing.CreatedAt, CreatedBy = existing.CreatedBy, DisplayName = existing.DisplayName,
                RoleTitle = existing.RoleTitle, IsActive = existing.IsActive, IsWorking = existing.IsWorking,
                Role = existing.Role, PayrollSnapshotVersion = existing.PayrollSnapshotVersion,
                AttendanceSource = existing.AttendanceSource, AttendanceRequestId = existing.AttendanceRequestId,
                AttendanceApprovedBy = existing.AttendanceApprovedBy, AttendanceApprovedAt = existing.AttendanceApprovedAt,
                PublicTipEligible = existing.PublicTipEligible, Note = existing.Note
            } : new SettlementStaffInputRow
            {
                SettlementId = settlementId, StaffId = input.StaffId, Role = input.Role,
                PayrollSnapshotVersion = assignment.PayrollSnapshotVersion, DisplayName = assignment.DisplayName,
                RoleTitle = assignment.RoleTitle, IsActive = assignment.IsActive, IsWorking = assignment.ActualMinutes > 0,
                AttendanceSource = "manual", PublicTipEligible = input.Role != "backstage"
            };
            if (input.ActualMinutesChanged && exists && input.ActualMinutes != existing!.ActualMinutes)
            {
                row.AttendanceSource = "manual";
                row.AttendanceRequestId = null;
                row.AttendanceApprovedBy = null;
                row.AttendanceApprovedAt = null;
            }
            if (input.ActualMinutesChanged) row.ActualMinutes = input.ActualMinutes;
            if (input.ActivityHoursChanged) row.ActivityHours = input.ActivityHours;
            if (input.BackstageParticipantChanged)
                row.IsBackstageParticipant = input.Role == "backstage" && assignment.CanUseBackstage
                    && input.IsBackstageParticipant;
            current[(input.StaffId, input.Role)] = row;
        }
        return current.Values.OrderBy(x => x.StaffId, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal).ToList();
    }

    private static bool RecalculationInputsEqual(IReadOnlyList<SettlementStaffInputRow> current,
        IReadOnlyList<SettlementStaffInputRow> proposed)
        => JsonSerializer.Serialize(current.OrderBy(x => x.StaffId, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal)
                .Select(x => new { x.StaffId, x.Role, x.ActualMinutes, x.ActivityHours, x.IsBackstageParticipant, x.Note }))
            == JsonSerializer.Serialize(proposed.OrderBy(x => x.StaffId, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal)
                .Select(x => new { x.StaffId, x.Role, x.ActualMinutes, x.ActivityHours, x.IsBackstageParticipant, x.Note }));

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
