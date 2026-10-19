using ToBeClarify.Api.Services.Ordering;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public sealed partial class SettlementService : ISettlementService
{
    private static readonly string[] InputRoles = ["designated", "service", "manager", "backstage"];
    private readonly ISettlementRepository _repository;
    private readonly IAppClock _clock;
    private readonly IBusinessDayContext _businessDays;
    private readonly IOrderingService _ordering;

    public SettlementService(ISettlementRepository repository, IAppClock clock, IBusinessDayContext businessDays,
        IOrderingService ordering)
    {
        _repository = repository;
        _clock = clock;
        _businessDays = businessDays;
        _ordering = ordering;
    }

    public async Task<SettlementOverviewDto> GetOverviewAsync(DateOnly businessDate, int sessionNo,
        CancellationToken cancellationToken)
    {
        ValidateDate(businessDate);
        if (sessionNo < 1) throw new BusinessException("營業時段必須大於零。", "SETTLEMENT_SESSION_INVALID");
        // Reading an unsaved date returns a preview; only write operations create a run.
        var run = await _repository.GetRunAsync(businessDate, sessionNo, cancellationToken)
            ?? new SettlementRunRow { BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo };
        var rule = await GetRuleForRunAsync(run, businessDate, cancellationToken);
        var source = await _repository.GetSourceDataAsync(run.Id, businessDate, cancellationToken);
        if (run.Status == "finalized")
            return MapOverview(run, rule, source, source.Results, BuildFinalizedSummary(run, source.Results));
        var calculation = Calculate(run, rule, source);
        return MapOverview(run, rule, source, calculation.Results, calculation.Summary, calculation.Anomalies);
    }

    public async Task<IReadOnlyList<SettlementRuleDto>> GetRulesAsync(string? dayType,
        CancellationToken cancellationToken)
        => (await _repository.GetRulesAsync(dayType, cancellationToken)).Select(MapRule).ToArray();

    public async Task<SettlementRuleDto> SaveRuleAsync(SaveSettlementRuleRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateRule(request);
        var rule = new SettlementRuleRow
        {
            Id = Guid.NewGuid().ToString("D"), DayType = request.DayType,
            EffectiveFrom = request.EffectiveFrom.ToDateTime(TimeOnly.MinValue),
            DesignatedHourlyRate = request.DesignatedHourlyRate,
            ServiceManagerHourlyRate = request.ServiceManagerHourlyRate,
            BackstageHourlyRate = request.BackstageHourlyRate,
            DesignatedSharePercentage = request.DesignatedSharePercentage,
            PublicRoomStaffPercentage = request.PublicRoomStaffPercentage,
            DedicatedRoomOwnerPercentage = request.DedicatedRoomOwnerPercentage,
            ServiceManagerPoolPercentage = request.ServiceManagerPoolPercentage,
            BackstagePoolPercentage = request.BackstagePoolPercentage,
            CompanyPercentage = request.CompanyPercentage,
            TimeRoundMinutes = request.TimeRoundMinutes,
            MoneyRoundUnit = 1,
            PublicTipMode = request.PublicTipMode
        };
        try
        {
            await _repository.InsertRuleAsync(rule, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("同一日期類型已有生效規則；歷史規則不可覆蓋，請改用新的生效日期。", "SETTLEMENT_RULE_OVERLAP");
        }
        return MapRule(rule);
    }

    public async Task<SettlementOverviewDto> SaveInputsAsync(SaveSettlementInputsRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        var dayType = request.DayType is "event" ? "event" : "normal";
        ValidateInputs(request);
        if (!await _repository.StaffMembersExistAsync(
            request.StaffInputs.Select(input => input.StaffId.Trim()).Distinct(StringComparer.Ordinal).ToArray(), cancellationToken))
            throw new BusinessException("結算人員不存在，請重新載入員工清單。", "SETTLEMENT_STAFF_NOT_FOUND");
        var run = await _repository.GetRunAsync(request.BusinessDate, request.SessionNo, cancellationToken);
        var rule = run is null
            ? await _repository.GetEffectiveRuleAsync(dayType, request.BusinessDate, cancellationToken)
            : await GetRuleForRunAsync(run, request.BusinessDate, cancellationToken);
        if (rule is null)
            throw new BusinessException("找不到該營業日生效的結算規則。", "SETTLEMENT_RULE_NOT_FOUND");
        run ??= await _repository.GetOrCreateRunAsync(request.BusinessDate, request.SessionNo, dayType, rule.Id,
            ActorId(actor), _clock.LocalDateTime, cancellationToken);
        EnsureEditable(run);
        await _repository.SaveInputsAsync(run.Id, new SaveInputsData
        {
            ExpectedInputVersion = request.ExpectedInputVersion,
            PublicTipAmount = request.PublicTipAmount,
            DayType = dayType,
            RuleVersionId = run.RuleVersionId ?? rule.Id,
            AdmissionFeeOverride = request.AdmissionFeeOverride,
            ActivityExpense = request.ActivityExpense,
            CompanyShareHours = request.CompanyShareHours,
            ActivityHoursConfirmed = request.ActivityHoursConfirmed,
            StaffInputs = request.StaffInputs.Select(MapInput).ToArray()
        }, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> CalculateAsync(SettlementCalculateRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(request.BusinessDate, request.SessionNo, actor, cancellationToken);
        EnsureEditable(run);
        var rule = await GetRuleForRunAsync(run, request.BusinessDate, cancellationToken);
        var source = await _repository.GetSourceDataAsync(run.Id, request.BusinessDate, cancellationToken);
        var calculation = Calculate(run, rule, source);
        await _repository.SaveCalculationAsync(run.Id, calculation.ToSave(rule, source.StaffInputs), ActorId(actor), _clock.LocalDateTime,
            cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> FinalizeAsync(SettlementFinalizeRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var calculated = await CalculateAsync(request, actor, cancellationToken);
        var period = await _businessDays.GetForDateAsync(request.BusinessDate, cancellationToken);
        if (period.BusinessPeriodId is not null && period.PeriodStatus == "open")
            throw new BusinessException("請先停止新單並完成實際關店，再進行正式結算。", "BUSINESS_PERIOD_NOT_CLOSED");
        if (calculated.Anomalies.Count > 0)
            throw new BusinessException("結算仍有需手動處理的異常，完成處理前不可正式結算。", "SETTLEMENT_REQUIRES_MANUAL_HANDLING");
        await _repository.FinalizeAsync(calculated.Run.Id, calculated.Run.InputVersion, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> ReopenAsync(SettlementReopenRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        try
        {
            var previous = await _repository.GetRunAsync(request.BusinessDate, request.SessionNo, cancellationToken)
                ?? throw new BusinessException("找不到要重新開放的結算時段。", "SETTLEMENT_NOT_FOUND");
            var rule = await _repository.GetEffectiveRuleAsync(previous.DayType, request.BusinessDate, cancellationToken)
                ?? throw new BusinessException("找不到重新開放時段當下有效的結算規則。", "SETTLEMENT_RULE_NOT_FOUND");
            var run = await _repository.ReopenAsync(request.BusinessDate, request.SessionNo, rule, request.Reason,
                ActorId(actor), _clock.LocalDateTime, cancellationToken);
            return await GetOverviewAsync(request.BusinessDate, run.SessionNo, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessException(ex.Message, "SETTLEMENT_REOPEN_NOT_ALLOWED");
        }
    }

    public async Task<SettlementOverviewDto> SaveOrderAdjustmentAsync(string orderId,
        SettlementOrderAdjustmentRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)) throw new BusinessException("訂單編號不可為空。", "ORDER_REQUIRED");
        var run = await RequireRunAsync(request.BusinessDate, request.SessionNo, actor, cancellationToken);
        EnsureEditable(run);
        _ = await _repository.GetOrderTotalAsync(orderId, cancellationToken)
            ?? throw new NotFoundException("找不到指定訂單。", "ORDER_NOT_FOUND");
        await _repository.SaveOrderAdjustmentAsync(run.Id, orderId, request.AdjustedAmount, request.Reason,
            request.Note, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> SubmitAttendanceBackfillAsync(
        SettlementAttendanceBackfillRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        var actorRole = actor.FindFirstValue(AdminAuthConstants.RoleClaimType);
        var actorStaffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        if (actorRole == AdminRole.Clerk && !string.Equals(actorStaffId, request.StaffId.Trim(), StringComparison.Ordinal))
            throw new UnauthorizedException();
        if (!await _repository.StaffMembersExistAsync([request.StaffId.Trim()], cancellationToken))
            throw new BusinessException("補打卡人員不存在，請重新載入員工清單。", "SETTLEMENT_STAFF_NOT_FOUND");
        var dayType = request.DayType is "normal" ? "normal" : "event";
        var rule = await _repository.GetEffectiveRuleAsync(dayType, request.BusinessDate, cancellationToken)
            ?? throw new BusinessException("找不到該營業日生效的結算規則。", "SETTLEMENT_RULE_NOT_FOUND");
        var run = await _repository.GetRunAsync(request.BusinessDate, request.SessionNo, cancellationToken)
            ?? await _repository.GetOrCreateRunAsync(request.BusinessDate, request.SessionNo, dayType, rule.Id,
                ActorId(actor), _clock.LocalDateTime, cancellationToken);
        EnsureEditable(run);
        var reason = request.Reason.Trim();
        if (reason.Length == 0) throw new BusinessException("補打卡必須填寫理由。", "ATTENDANCE_REASON_REQUIRED");
        await _repository.SubmitAttendanceBackfillAsync(run.Id, request.StaffId.Trim(), request.Role,
            request.RequestedMinutes, reason, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> ReviewAttendanceBackfillAsync(string requestId,
        SettlementAttendanceReviewRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (string.IsNullOrWhiteSpace(requestId))
            throw new BusinessException("補打卡申請編號不可為空。", "ATTENDANCE_REQUEST_REQUIRED");
        try
        {
            await _repository.ReviewAttendanceBackfillAsync(requestId, request.Approved, request.Note?.Trim(),
                ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessException(ex.Message, "ATTENDANCE_REVIEW_NOT_ALLOWED");
        }
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    private async Task<SettlementRunRow> RequireRunAsync(DateOnly date, int sessionNo, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateDate(date);
        var run = await _repository.GetRunAsync(date, sessionNo, cancellationToken);
        if (run is not null) return run;
        var rule = await _repository.GetEffectiveRuleAsync("normal", date, cancellationToken)
            ?? throw new BusinessException("找不到該營業日生效的結算規則。", "SETTLEMENT_RULE_NOT_FOUND");
        return await _repository.GetOrCreateRunAsync(date, sessionNo, "normal", rule.Id, ActorId(actor),
            _clock.LocalDateTime, cancellationToken);
    }

    private async Task<SettlementRuleRow> GetRuleForRunAsync(SettlementRunRow run, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(run.RuleSnapshotJson))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<SettlementRuleRow>(run.RuleSnapshotJson);
                if (snapshot is not null) return snapshot;
            }
            catch (JsonException) { /* A malformed snapshot is surfaced below as a manual anomaly. */ }
        }
        return await _repository.GetEffectiveRuleAsync(run.DayType, businessDate, cancellationToken)
            ?? throw new BusinessException("找不到該營業日生效的結算規則。", "SETTLEMENT_RULE_NOT_FOUND");
    }

    private CalculationResult Calculate(SettlementRunRow run, SettlementRuleRow rule, SettlementSourceData source)
    {
        var anomalies = new List<SettlementAnomalyDto>(source.AttendanceAnomalies);
        var orderMap = source.Orders.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        var itemMap = source.Items.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var nomineeMap = source.Nominees.GroupBy(x => x.OrderId).ToDictionary(x => x.Key,
            x => x.Select(n => n.StaffId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var staffNames = source.Nominees.GroupBy(x => x.StaffId).ToDictionary(x => x.Key, x => x.First().StaffName,
            StringComparer.Ordinal);
        foreach (var assignment in source.PayrollAssignments) staffNames[assignment.StaffId] = assignment.DisplayName;
        foreach (var input in source.StaffInputs) staffNames[input.StaffId] = input.DisplayName;
        var designatedBase = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var designatedShare = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var designatedOrders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var champagneBase = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var designatedTips = source.Tips.Where(x => !string.IsNullOrWhiteSpace(x.StaffId))
            .GroupBy(x => x.StaffId!, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (decimal)x.Sum(t => t.StaffAmount), StringComparer.Ordinal);
        decimal directCompany = 0;
        decimal mealRevenue = 0;
        decimal dedicatedGross = 0;
        decimal dedicatedOwnerShare = 0;
        decimal dedicatedCompanyRemainder = 0;
        var dedicatedShareByOwner = new Dictionary<string, decimal>(StringComparer.Ordinal);
        decimal publicRoomUnassigned = 0;
        decimal unknownRevenue = 0;
        decimal AmountFor(string orderId, int rawAmount, bool eligibleMeal = false)
        {
            if (!orderMap.TryGetValue(orderId, out var order)) return rawAmount;
            var collected = order.AdjustedAmount ?? order.TotalAmount;
            var revenueCollected = Math.Max(0, collected - order.TipAmount);
            if (itemMap.TryGetValue(orderId, out var lines) && lines.Any(x => x.IsChampagneTower))
            {
                var eligibleTotal = lines.Where(x => (x.ItemType is "menu_item" or "menu_set") && !x.IsChampagneTower).Sum(x => (decimal)x.LineTotal);
                var credit = eligibleMeal && eligibleTotal > 0 ? order.MealCreditApplied * rawAmount / eligibleTotal : 0;
                var payableSubtotal = Math.Max(0, order.TotalAmount - order.TipAmount);
                return payableSubtotal <= 0 ? 0 : Math.Max(0, rawAmount - credit) * revenueCollected / payableSubtotal;
            }
            var revenueSubtotal = Math.Max(0, order.Subtotal - order.TipAmount);
            return revenueSubtotal <= 0 ? 0 : rawAmount * (decimal)revenueCollected / revenueSubtotal;
        }

        void AddDesignated(string staffId, decimal amount, decimal percentage, string orderId)
        {
            if (string.IsNullOrWhiteSpace(staffId)) return;
            if (!designatedOrders.TryGetValue(staffId, out var orders))
                designatedOrders[staffId] = orders = new HashSet<string>(StringComparer.Ordinal);
            orders.Add(orderId);
            designatedBase[staffId] = designatedBase.GetValueOrDefault(staffId) + amount;
            designatedShare[staffId] = designatedShare.GetValueOrDefault(staffId) + amount * percentage / 100m;
        }

        foreach (var orderItems in itemMap.Values)
        {
            var baseStaffByItem = orderItems.Where(i => i.ItemType == "nomination_base" && !string.IsNullOrWhiteSpace(i.ReferenceId))
                .ToDictionary(i => i.ItemId, i => i.ReferenceId!, StringComparer.Ordinal);
            foreach (var item in orderItems)
            {
                if (item.ItemType == "tip" || item.ItemType == "room_service" || item.ItemType == "staff_service_addon") continue;
                var amount = AmountFor(item.OrderId, item.LineTotal,
                    (item.ItemType is "menu_item" or "menu_set") && !item.IsChampagneTower);
                if (item.ItemType == "nomination_base")
                {
                    AddDesignated(item.ReferenceId ?? string.Empty, amount, rule.DesignatedSharePercentage, item.OrderId);
                    directCompany += amount * (100m - rule.DesignatedSharePercentage) / 100m;
                }
                else if (item.ItemType == "staff_service")
                {
                    var staffId = item.ParentItemId is not null && baseStaffByItem.TryGetValue(item.ParentItemId, out var parentStaff)
                        ? parentStaff
                        : nomineeMap.GetValueOrDefault(item.OrderId, []).Length == 1 ? nomineeMap[item.OrderId][0] : string.Empty;
                    if (string.IsNullOrWhiteSpace(staffId))
                        anomalies.Add(new SettlementAnomalyDto("SERVICE_STAFF_UNRESOLVED", "服務項目找不到唯一指名人員。", item.ItemId));
                    else
                    {
                        AddDesignated(staffId, amount, rule.DesignatedSharePercentage, item.OrderId);
                        directCompany += amount * (100m - rule.DesignatedSharePercentage) / 100m;
                    }
                }
                else if (item.IsChampagneTower && item.RecipientStaffId is not null)
                {
                    champagneBase[item.RecipientStaffId] = champagneBase.GetValueOrDefault(item.RecipientStaffId) + amount;
                    directCompany += amount * (100m - rule.DesignatedSharePercentage) / 100m;
                    staffNames.TryAdd(item.RecipientStaffId, item.RecipientStaffName ?? item.RecipientStaffId);
                }
                else if (item.ItemType is "menu_item" or "menu_set") mealRevenue += amount;
                else
                {
                    unknownRevenue += amount;
                    anomalies.Add(new SettlementAnomalyDto("UNCLASSIFIED_ORDER_ITEM", $"未分類營業項目：{item.ItemType}。", item.ItemId));
                }
            }
        }

        foreach (var addon in source.Addons)
        {
            var amount = AmountFor(addon.OrderId, addon.LineTotal);
            AddDesignated(addon.StaffId, amount, rule.DesignatedSharePercentage, addon.OrderId);
            directCompany += amount * (100m - rule.DesignatedSharePercentage) / 100m;
            staffNames.TryAdd(addon.StaffId, addon.StaffId);
        }

        foreach (var room in source.Rooms)
        {
            var rawAmount = room.OrderItemId is not null && itemMap.TryGetValue(room.OrderId ?? string.Empty, out var orderItems)
                ? orderItems.FirstOrDefault(i => i.ItemId == room.OrderItemId)?.LineTotal ?? (int)room.TotalAmount
                : (int)room.TotalAmount;
            var amount = room.OrderId is null ? room.TotalAmount : AmountFor(room.OrderId, rawAmount);
            if (!string.IsNullOrWhiteSpace(room.OwnerStaffId))
            {
                dedicatedGross += amount;
                var ownerShare = amount * rule.DedicatedRoomOwnerPercentage / 100m;
                dedicatedOwnerShare += ownerShare;
                dedicatedCompanyRemainder += amount - ownerShare;
                dedicatedShareByOwner[room.OwnerStaffId] = dedicatedShareByOwner.GetValueOrDefault(room.OwnerStaffId) + ownerShare;
                staffNames.TryAdd(room.OwnerStaffId, room.OwnerStaffName ?? room.OwnerStaffId);
                if (string.IsNullOrWhiteSpace(room.OwnerStaffName))
                    anomalies.Add(new SettlementAnomalyDto("DEDICATED_ROOM_OWNER_INVALID", "專屬包廂找不到有效擁有者。", room.Id));
                continue;
            }
            var nominees = room.OrderId is not null ? nomineeMap.GetValueOrDefault(room.OrderId, []) : [];
            if (nominees.Length == 1)
            {
                AddDesignated(nominees[0], amount, rule.PublicRoomStaffPercentage, room.OrderId!);
                directCompany += amount * (100m - rule.PublicRoomStaffPercentage) / 100m;
                staffNames.TryAdd(nominees[0], nominees[0]);
            }
            else
            {
                publicRoomUnassigned += amount;
                if (nominees.Length > 1)
                    anomalies.Add(new SettlementAnomalyDto("PUBLIC_ROOM_STAFF_UNRESOLVED", "公共包廂對應多位指名人員，需手動指定歸屬。", room.Id));
            }
        }

        var admission = source.Admissions.FirstOrDefault();
        var admissionFee = admission?.UseAdmissionLedger == true ? 0 : run.AdmissionFeeOverride ?? ParseMoney(admission?.AdmissionFeeText);
        var admissionRevenue = admission?.UseAdmissionLedger == true
            ? admission.LedgerReceivable : (admission?.SessionCount ?? 0) * admissionFee;
        if (admission?.UseAdmissionLedger == true && admission.UnrecordedCount > 0)
            anomalies.Add(new SettlementAnomalyDto("ADMISSION_LEDGER_INCOMPLETE", $"有 {admission.UnrecordedCount} 筆入場帳未登錄，未推定已收款。", null));
        if (admission?.UseAdmissionLedger == true && run.AdmissionFeeOverride is not null)
            anomalies.Add(new SettlementAnomalyDto("ADMISSION_OVERRIDE_IGNORED", "新版入場收入依逐筆入場帳，未套用人數乘單價覆寫。", null));
        if (admission?.UseAdmissionLedger == true && admission.LedgerReceivable < 0)
            anomalies.Add(new SettlementAnomalyDto("ADMISSION_LEDGER_NEGATIVE", "入場折讓超過應收，請先核對入場帳。", null));
        var designatedRevenueBase = designatedBase.Values.Sum() + champagneBase.Values.Sum();
        var companyRevenue = directCompany + dedicatedCompanyRemainder + publicRoomUnassigned + admissionRevenue + mealRevenue + unknownRevenue;
        var servicePool = companyRevenue * rule.ServiceManagerPoolPercentage / 100m;
        var backstagePool = companyRevenue * rule.BackstagePoolPercentage / 100m;
        var companyIncome = companyRevenue * rule.CompanyPercentage / 100m;
        var grossRevenue = designatedRevenueBase + dedicatedGross + publicRoomUnassigned + admissionRevenue + mealRevenue + unknownRevenue;
        var runCopy = CopyRun(run);
        runCopy.CalculationVersion = 2;
        runCopy.RuleVersionId = rule.Id;
        runCopy.RuleSnapshotJson = JsonSerializer.Serialize(rule);
        runCopy.AdmissionFeeSnapshot = admissionFee;
        runCopy.GrossRevenue = grossRevenue;
        runCopy.DesignatedRevenueBase = designatedRevenueBase;
        runCopy.DedicatedRoomGross = dedicatedGross;
        runCopy.DedicatedRoomOwnerShare = dedicatedOwnerShare;
        runCopy.DedicatedRoomCompanyRemainder = dedicatedCompanyRemainder;
        runCopy.PublicRoomUnassignedRevenue = publicRoomUnassigned;
        runCopy.AdmissionRevenue = admissionRevenue;
        runCopy.MealRevenue = mealRevenue;
        runCopy.CompanyRevenue = companyRevenue;
        runCopy.ServiceManagerPool = servicePool;
        runCopy.BackstagePool = backstagePool;
        runCopy.CompanyIncome = companyIncome;
        runCopy.BusinessPeriodId = source.Period.BusinessPeriodId ?? source.Cash.BusinessPeriodId;
        runCopy.SourceVersion = source.Cash.SourceVersion;
        runCopy.SourceCutoffAt = source.Cash.LatestRecordedAt;
        runCopy.CashReceived = source.Cash.CashReceived;
        runCopy.CashRefunded = source.Cash.CashRefunded;
        runCopy.NetCash = source.Cash.NetCash;
        runCopy.RetainedAmount = Math.Max(0, source.Cash.RetainedAmount);
        runCopy.PendingFinanceCount = source.Cash.PendingFinanceCount;

        var champagneShare = champagneBase.Values.Sum() * rule.DesignatedSharePercentage / 100m;
        var results = (run.DayType == "event"
            ? CalculateEventResults(runCopy, source, rule, grossRevenue - champagneShare, anomalies)
            : CalculateNormalResults(runCopy, source, rule, designatedBase, designatedShare, designatedTips,
                servicePool, backstagePool, dedicatedShareByOwner, staffNames, designatedOrders, anomalies)).ToList();
        runCopy.ActivityNetRevenue = run.DayType == "event" ? grossRevenue - champagneShare - run.ActivityExpense : 0;
        foreach (var recipient in champagneBase)
            results.Add(MakeResult(runCopy, recipient.Key, "champagne_recipient", 0, 0, recipient.Value,
                rule.DesignatedSharePercentage, recipient.Value * rule.DesignatedSharePercentage / 100m,
                0, 0, 1, staffNames.GetValueOrDefault(recipient.Key), "normal", null));
        if (run.DayType == "event")
        {
            var activityHours = source.StaffInputs.Where(x => x.ActivityHours is > 0).Sum(x => x.ActivityHours!.Value);
            var activityDenominator = activityHours + (run.CompanyShareHours ?? 0);
            runCopy.CompanyRevenue = 0;
            runCopy.ServiceManagerPool = 0;
            runCopy.BackstagePool = 0;
            runCopy.CompanyIncome = activityDenominator <= 0 ? 0 : runCopy.ActivityNetRevenue * (run.CompanyShareHours ?? 0) / activityDenominator;
        }
        foreach (var tip in designatedTips)
        {
            var primary = source.StaffInputs.FirstOrDefault(input => input.StaffId == tip.Key
                && source.PayrollAssignments.Any(a => a.StaffId == tip.Key && a.PayrollRole == input.Role));
            if (primary is null)
                anomalies.Add(new SettlementAnomalyDto("PAYROLL_INPUT_MISSING", "有指定小費但缺少符合上班職位的主要計薪輸入。", tip.Key, staffNames.GetValueOrDefault(tip.Key)));
            var assigned = results.Where(x => x.StaffId == tip.Key).Sum(x => x.DesignatedTip);
            if (assigned < tip.Value)
                results.Add(MakeResult(runCopy, tip.Key, "designated_tip", 0, 0, 0, 0, 0,
                    tip.Value - assigned, 0, 1, staffNames.GetValueOrDefault(tip.Key), "normal", null));
            else if (assigned > tip.Value)
                anomalies.Add(new SettlementAnomalyDto("DESIGNATED_TIP_DUPLICATED", "指定小費分配超過來源金額，請核對收入列。", tip.Key));
        }
        if (source.Cash.HasCashRecords || admission?.UseAdmissionLedger == true)
            ApplyCashSnapshot(runCopy, results, source.Items.Any(x => x.IsChampagneTower));
        var summary = new SettlementSummaryDto(runCopy.GrossRevenue, runCopy.DesignatedRevenueBase,
            runCopy.DedicatedRoomGross, runCopy.DedicatedRoomOwnerShare, runCopy.DedicatedRoomCompanyRemainder,
            runCopy.PublicRoomUnassignedRevenue, runCopy.AdmissionRevenue, runCopy.MealRevenue, runCopy.CompanyRevenue,
            runCopy.ServiceManagerPool, runCopy.BackstagePool, runCopy.CompanyIncome, runCopy.ActivityNetRevenue,
            results.Sum(x => x.AfterRounding), results.Sum(x => x.CompanySubsidy),
            0, 0,
            runCopy.CashReceived, runCopy.CashRefunded, runCopy.NetCash, runCopy.RetainedAmount,
            runCopy.PendingFinanceCount);
        // Keep diagnostic context independent of the payroll formula and anomaly creation path.
        var detailedAnomalies = anomalies.DistinctBy(x => (x.Code, x.SourceId)).Select(anomaly =>
        {
            if (anomaly.SourceId is not { } staffId || anomaly.Code is not
                ("PAYROLL_INPUT_MISSING" or "DESIGNATED_INPUT_MISSING" or "PAYROLL_ROLE_UNRESOLVED"
                or "PAYROLL_INPUT_CONFLICT" or "BACKSTAGE_NOT_ALLOWED" or "PAYROLL_SNAPSHOT_CHANGED"
                or "DESIGNATED_TIP_DUPLICATED" or "EVENT_ATTENDANCE_NOT_VERIFIED")) return anomaly;
            var orderIds = (designatedOrders.TryGetValue(staffId, out var revenueOrders)
                    ? revenueOrders.AsEnumerable() : Enumerable.Empty<string>())
                .Concat(source.Tips.Where(x => x.StaffId == staffId).Select(x => x.OrderId))
                .ToHashSet(StringComparer.Ordinal);
            var orders = source.Orders.Where(x => orderIds.Contains(x.OrderId))
                .DistinctBy(x => x.OrderId).OrderBy(x => x.OrderNumber, StringComparer.Ordinal)
                .Select(x => new SettlementAnomalyOrderDto(x.OrderId, x.OrderNumber)).ToArray();
            return anomaly with { StaffName = anomaly.StaffName ?? staffNames.GetValueOrDefault(staffId), Orders = orders };
        }).ToArray();
        return new CalculationResult(runCopy, results, summary, detailedAnomalies);
    }

    public async Task<SettlementOverviewDto> CloseAsync(SettlementCloseRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        var period = await _businessDays.GetForDateAsync(request.BusinessDate, cancellationToken);
        if (period.BusinessPeriodId is null)
            throw new BusinessException("本營業日尚未開店，無法關店。", "BUSINESS_PERIOD_NOT_FOUND");
        if (period.PeriodStatus == "open")
        {
            await _ordering.ApplyBusinessPeriodActionAsync(new BusinessPeriodActionRequest
            {
                BusinessDate = request.BusinessDate, Action = "set_intake_mode", IntakeMode = "staff_only",
                Reason = request.Reason?.Trim() ?? "關店前停止新單", OperationId = request.OperationId
            }, actor, cancellationToken);
            await _ordering.ApplyBusinessPeriodActionAsync(new BusinessPeriodActionRequest
            {
                BusinessDate = request.BusinessDate, Action = "close",
                Reason = request.Reason?.Trim() ?? "實際關店"
            }, actor, cancellationToken);
        }
        return await GetOverviewAsync(request.BusinessDate, 1, cancellationToken);
    }

    public async Task<SettlementPaymentDto> RecordPaymentAsync(SettlementPayoutRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.StaffId) || string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessException("支付金額、人員與原因不可為空。", "SETTLEMENT_PAYMENT_INVALID");
        var run = await RequireRunAsync(request.BusinessDate, request.SessionNo, actor, cancellationToken);
        if (run.Status != "finalized")
            throw new BusinessException("請先完成正式結算，才能登記支付或追回。", "SETTLEMENT_PAYMENT_REQUIRES_FINALIZED");
        var row = await _repository.RecordPaymentAsync(run.Id, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return new SettlementPaymentDto(row.Id, row.SettlementId, row.StaffId, row.EventKind, row.Amount,
            row.OperationId, row.Reason, ToOffset(row.CreatedAt), row.CreatedBy);
    }

    public async Task<SettlementCorrectionDto> RecordCorrectionAsync(SettlementCorrectionRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (request.AmountDelta == 0 || string.IsNullOrWhiteSpace(request.SourceKind) || string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessException("更正差額、來源與原因不可為空。", "SETTLEMENT_CORRECTION_INVALID");
        var run = await RequireRunAsync(request.BusinessDate, request.SessionNo, actor, cancellationToken);
        if (run.Status != "finalized")
            throw new BusinessException("只有正式結算後的差異需要建立更正版本。", "SETTLEMENT_CORRECTION_REQUIRES_FINALIZED");
        var row = await _repository.RecordCorrectionAsync(run.Id, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return new SettlementCorrectionDto(row.Id, row.SettlementId, row.CorrectionOfId, row.SourceKind,
            row.SourceId, row.StaffId, row.AmountDelta, row.Status, row.OperationId, row.Reason,
            ToOffset(row.CreatedAt), row.CreatedBy);
    }

    private static void ApplyCashSnapshot(SettlementRunRow run, IReadOnlyList<SettlementResultLineRow> results, bool hasChampagne)
    {
        var splitActivity = run.DayType == "event" && hasChampagne;
        var calculatedGross = run.GrossRevenue;
        var allocatableCash = Math.Max(0, run.NetCash - run.RetainedAmount);
        var factor = calculatedGross <= 0 ? 0 : allocatableCash / calculatedGross;
        if (calculatedGross <= 0 && allocatableCash > 0) factor = 1;
        run.GrossRevenue = calculatedGross * factor;
        run.DesignatedRevenueBase *= factor;
        run.DedicatedRoomGross *= factor;
        run.DedicatedRoomOwnerShare *= factor;
        run.DedicatedRoomCompanyRemainder *= factor;
        run.PublicRoomUnassignedRevenue *= factor;
        run.AdmissionRevenue *= factor;
        run.MealRevenue *= factor;
        run.CompanyRevenue *= factor;
        run.ServiceManagerPool *= factor;
        run.BackstagePool *= factor;
        run.CompanyIncome *= factor;
        run.ActivityNetRevenue = splitActivity
            ? (run.ActivityNetRevenue + run.ActivityExpense) * factor - run.ActivityExpense
            : run.GrossRevenue - run.ActivityExpense;
        var activityDenominator = results.Where(x => x.Role != "champagne_recipient").Sum(x => x.RevenueBase)
            + (run.CompanyShareHours ?? 0);
        if (splitActivity) run.CompanyIncome = activityDenominator <= 0 ? 0
            : run.ActivityNetRevenue * (run.CompanyShareHours ?? 0) / activityDenominator;
        foreach (var result in results)
        {
            if (splitActivity && result.Role != "champagne_recipient")
                result.RevenueShare = activityDenominator <= 0 ? 0 : run.ActivityNetRevenue * result.RevenueBase / activityDenominator;
            else
            {
                result.RevenueBase *= factor;
                result.RevenueShare *= factor;
            }
            result.PreferredPay = CalculateRolePay(result.Role, result.BasePay, result.RevenueShare);
            result.BeforeRounding = result.PreferredPay + result.DesignatedTip + result.PublicTip;
            result.AfterRounding = checked((int)Math.Ceiling(result.BeforeRounding));
            result.CompanySubsidy = result.AfterRounding - result.BeforeRounding;
        }
    }

    private static IReadOnlyList<SettlementResultLineRow> CalculateNormalResults(SettlementRunRow run,
        SettlementSourceData source, SettlementRuleRow rule, IReadOnlyDictionary<string, decimal> designatedBase,
        IReadOnlyDictionary<string, decimal> designatedShare, IReadOnlyDictionary<string, decimal> designatedTips,
        decimal servicePool, decimal backstagePool, IReadOnlyDictionary<string, decimal> dedicatedShareByOwner,
        IReadOnlyDictionary<string, string> staffNames, IReadOnlyDictionary<string, HashSet<string>> designatedOrders,
        List<SettlementAnomalyDto> anomalies)
    {
        var inputs = source.StaffInputs;
        var results = new Dictionary<(string? StaffId, string Role), SettlementResultLineRow>();
        var payable = inputs.ToDictionary(x => (x.StaffId, x.Role), x => PayableHours(x.ActualMinutes, rule.TimeRoundMinutes));
        var tipHours = inputs.Where(x => IsPublicTipRole(x.Role) && IsPublicTipEligible(x, payable.GetValueOrDefault((x.StaffId, x.Role))))
            .Sum(x => payable.GetValueOrDefault((x.StaffId, x.Role)));
        var publicTips = run.PublicTipAmount;
        if (publicTips > 0 && tipHours <= 0)
            anomalies.Add(new SettlementAnomalyDto("PUBLIC_TIP_DENOMINATOR_ZERO", "公共小費分配總工時為 0，需手動處理。"));
        var serviceManagerHours = inputs.Where(x => x.Role is "service" or "manager")
            .Sum(x => payable.GetValueOrDefault((x.StaffId, x.Role)));
        if (servicePool > 0 && serviceManagerHours <= 0)
            anomalies.Add(new SettlementAnomalyDto("SERVICE_MANAGER_DENOMINATOR_ZERO", "服務生／經理合計工時為 0，需手動處理。"));
        var backstage = inputs.Where(x => x.Role == "backstage" && x.IsBackstageParticipant).ToArray();
        if (backstagePool > 0 && backstage.Length == 0)
            anomalies.Add(new SettlementAnomalyDto("BACKSTAGE_DENOMINATOR_ZERO", "幕後技術人員參與人數為 0，需手動處理。"));

        var assignments = source.PayrollAssignments.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        var primary = inputs.Where(x => assignments.TryGetValue(x.StaffId, out var assignment) && x.Role == assignment.PayrollRole).ToArray();
        foreach (var input in primary)
        {
            var hours = payable.GetValueOrDefault((input.StaffId, input.Role));
            var share = input.Role == "designated" ? designatedShare.GetValueOrDefault(input.StaffId)
                : input.Role is "service" or "manager" ? (serviceManagerHours <= 0 ? 0 : servicePool * hours / serviceManagerHours)
                : input.IsBackstageParticipant && backstage.Length > 0 ? backstagePool / backstage.Length : 0;
            var rate = input.Role == "designated" ? rule.DesignatedHourlyRate
                : input.Role is "service" or "manager" ? rule.ServiceManagerHourlyRate : 0;
            AddResult(results, run, input.StaffId, input.Role, input.Role == "backstage" ? 0 : hours, rate,
                input.Role == "designated" ? designatedBase.GetValueOrDefault(input.StaffId) : 0,
                input.Role == "designated" ? rule.DesignatedSharePercentage
                    : input.Role == "backstage" ? rule.BackstagePoolPercentage : rule.ServiceManagerPoolPercentage,
                share, designatedTips.GetValueOrDefault(input.StaffId),
                PublicTipFor(input, hours, publicTips, tipHours), 1, staffNames.GetValueOrDefault(input.StaffId));
        }
        foreach (var input in backstage.Where(x => assignments.TryGetValue(x.StaffId, out var a) && a.PayrollRole != "backstage" && a.CanUseBackstage))
            AddResult(results, run, input.StaffId, "backstage", 0, 0, 0, rule.BackstagePoolPercentage,
                backstagePool / backstage.Length, 0, 0, 1, staffNames.GetValueOrDefault(input.StaffId));
        foreach (var staffId in designatedBase.Keys.Concat(designatedTips.Keys).Distinct(StringComparer.Ordinal))
        {
            var input = primary.FirstOrDefault(x => x.StaffId == staffId);
            if (input is null)
                anomalies.Add(new SettlementAnomalyDto("PAYROLL_INPUT_MISSING", "有服務所得或指定小費，但缺少符合上班職位的主要計薪輸入。", staffId, staffNames.GetValueOrDefault(staffId)));
            if (input?.Role == "designated") continue;
            var share = designatedShare.GetValueOrDefault(staffId);
            if (share > 0)
                AddResult(results, run, staffId, "support_service_share", 0, 0, designatedBase.GetValueOrDefault(staffId),
                    rule.DesignatedSharePercentage, share, 0, 0, 1, staffNames.GetValueOrDefault(staffId));
            if (input is null && designatedTips.GetValueOrDefault(staffId) > 0)
                AddResult(results, run, staffId, "designated_tip", 0, 0, 0, 0, 0, designatedTips[staffId], 0, 1, staffNames.GetValueOrDefault(staffId));
        }
        if (dedicatedShareByOwner.Count > 0)
        {
            foreach (var owner in source.Rooms.Where(x => !string.IsNullOrWhiteSpace(x.OwnerStaffId))
                         .GroupBy(x => x.OwnerStaffId!, StringComparer.Ordinal))
            {
                var share = dedicatedShareByOwner.GetValueOrDefault(owner.Key);
                AddResult(results, run, owner.Key, "dedicated_room_owner", 0, 0, owner.Sum(x => x.TotalAmount),
                    rule.DedicatedRoomOwnerPercentage, share, 0, 0, 1,
                    staffNames.GetValueOrDefault(owner.Key));
            }
        }
        return results.Values.ToArray();
    }

    private static IReadOnlyList<SettlementResultLineRow> CalculateEventResults(SettlementRunRow run,
        SettlementSourceData source, SettlementRuleRow rule, decimal grossRevenue, List<SettlementAnomalyDto> anomalies)
    {
        if (!run.ActivityHoursConfirmed || run.CompanyShareHours is null)
            anomalies.Add(new SettlementAnomalyDto("EVENT_ACTIVITY_INPUT_REQUIRED", "活動日需要確認個人活動分配時數與公司份額時數。"));
        var activityHours = source.StaffInputs.Where(x => x.ActivityHours is > 0)
            .ToDictionary(x => (x.StaffId, x.Role), x => x.ActivityHours!.Value);
        var totalHours = activityHours.Values.Sum() + (run.CompanyShareHours ?? 0);
        if (totalHours <= 0) anomalies.Add(new SettlementAnomalyDto("EVENT_DENOMINATOR_ZERO", "活動日分配基礎時數為 0。"));
        foreach (var input in source.StaffInputs.Where(x => x.ActualMinutes > 0 || x.ActivityHours is > 0))
        {
            if (input.AttendanceSource is not ("clock" or "backfill_approved"))
                anomalies.Add(new SettlementAnomalyDto("EVENT_ATTENDANCE_NOT_VERIFIED",
                    "活動日出席必須有打卡或已核准的補打卡資料。", input.StaffId));
        }
        var assignments = source.PayrollAssignments.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        var payable = source.StaffInputs.ToDictionary(x => (x.StaffId, x.Role),
            x => PayableHours(x.ActualMinutes, rule.TimeRoundMinutes));
        var publicTipHours = source.StaffInputs
            .Where(x => IsPublicTipRole(x.Role) && IsPublicTipEligible(x, payable.GetValueOrDefault((x.StaffId, x.Role))))
            .Sum(x => payable.GetValueOrDefault((x.StaffId, x.Role)));
        if (run.PublicTipAmount > 0 && publicTipHours <= 0)
            anomalies.Add(new SettlementAnomalyDto("PUBLIC_TIP_DENOMINATOR_ZERO", "公共小費分配總工時為 0，需手動處理。"));
        var designatedTips = source.Tips.Where(x => !string.IsNullOrWhiteSpace(x.StaffId))
            .GroupBy(x => x.StaffId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (decimal)x.Sum(t => t.StaffAmount), StringComparer.Ordinal);
        return source.StaffInputs.Where(x => activityHours.ContainsKey((x.StaffId, x.Role)))
            .Select(input =>
            {
                var isPrimary = assignments.TryGetValue(input.StaffId, out var assignment) && input.Role == assignment.PayrollRole;
                var hours = isPrimary ? payable.GetValueOrDefault((input.StaffId, input.Role)) : 0;
                var activityShare = totalHours <= 0 ? 0 : (grossRevenue - run.ActivityExpense)
                    * activityHours[(input.StaffId, input.Role)] / totalHours;
                var hourlyRate = input.Role is "service" or "manager"
                    ? rule.ServiceManagerHourlyRate
                    : input.Role == "designated" ? rule.DesignatedHourlyRate : rule.BackstageHourlyRate;
                var publicTip = PublicTipFor(input, hours, run.PublicTipAmount, publicTipHours);
                return MakeResult(run, input.StaffId, input.Role, hours, hourlyRate, activityHours[(input.StaffId, input.Role)],
                    100, activityShare, isPrimary ? designatedTips.GetValueOrDefault(input.StaffId) : 0, publicTip, 1,
                    input.DisplayName, anomalies.Count > 0 ? "manual" : "normal",
                    anomalies.Count > 0 ? "活動日仍有待確認輸入。" : null);
            }).ToArray();
    }

    private static void AddResult(Dictionary<(string? StaffId, string Role), SettlementResultLineRow> results,
        SettlementRunRow run, string staffId, string role, decimal hours, int hourlyRate, decimal revenueBase,
        decimal percentage, decimal revenueShare, decimal designatedTip, decimal publicTip, int moneyUnit,
        string? displayName)
    {
        results[(staffId, role)] = MakeResult(run, staffId, role, hours, hourlyRate, revenueBase, percentage,
            revenueShare, designatedTip, publicTip, moneyUnit, displayName, "normal", null);
    }

    private static SettlementResultLineRow MakeResult(SettlementRunRow run, string? staffId, string role,
        decimal hours, int hourlyRate, decimal revenueBase, decimal percentage, decimal revenueShare,
        decimal designatedTip, decimal publicTip, int moneyUnit, string? displayName, string anomalyStatus,
        string? anomalyNote)
    {
        var basePay = hours * hourlyRate;
        var preferred = CalculateRolePay(role, basePay, revenueShare);
        var before = preferred + designatedTip + publicTip;
        var after = checked((int)Math.Ceiling(before));
        return new SettlementResultLineRow
        {
            Id = Guid.NewGuid().ToString("D"), SettlementId = run.Id, StaffId = staffId, DisplayName = displayName,
            Role = role, PayableHours = hours, HourlyRate = hourlyRate, BasePay = basePay, RevenueBase = revenueBase,
            RevenuePercentage = percentage, RevenueShare = revenueShare, DesignatedTip = designatedTip,
            PublicTip = publicTip, PreferredPay = preferred, BeforeRounding = before, AfterRounding = after,
            CompanySubsidy = after - before, AnomalyStatus = anomalyStatus, AnomalyNote = anomalyNote
        };
    }

    private static decimal CalculateRolePay(string role, decimal basePay, decimal revenueShare)
        => role switch
        {
            "service" or "manager" => basePay + revenueShare,
            "dedicated_room_owner" => revenueShare,
            _ => Math.Max(basePay, revenueShare)
        };

    private static decimal PublicTipFor(SettlementStaffInputRow? input, decimal hours, decimal publicTips, decimal totalHours)
        => input is null || !IsPublicTipEligible(input, hours) || totalHours <= 0 ? 0 : publicTips * hours / totalHours;

    private static bool IsPublicTipRole(string role) => role is "designated" or "service" or "manager";

    private static bool IsPublicTipEligible(SettlementStaffInputRow input, decimal payableHours)
        => IsPublicTipRole(input.Role) && payableHours > 0;

    private static decimal PayableHours(int actualMinutes, int roundMinutes)
    {
        if (actualMinutes <= 0) return 0;
        var whole = actualMinutes / 60;
        var remainder = actualMinutes % 60;
        return whole + (remainder >= Math.Max(1, roundMinutes) ? 1 : 0);
    }

    private static int ParseMoney(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var result) ? result : 0;
    }

    private static SettlementRunRow CopyRun(SettlementRunRow row) => new()
    {
        Id = row.Id, BusinessDate = row.BusinessDate, SessionNo = row.SessionNo, DayType = row.DayType,
        InputVersion = row.InputVersion, CalculationVersion = row.CalculationVersion,
        FinalizedAt = row.FinalizedAt, FinalizedBy = row.FinalizedBy,
        Status = row.Status, RuleVersionId = row.RuleVersionId, RuleSnapshotJson = row.RuleSnapshotJson,
        PublicTipAmount = row.PublicTipAmount, AdmissionFeeOverride = row.AdmissionFeeOverride,
        AdmissionFeeSnapshot = row.AdmissionFeeSnapshot, ActivityExpense = row.ActivityExpense,
        CompanyShareHours = row.CompanyShareHours, ActivityHoursConfirmed = row.ActivityHoursConfirmed,
        BusinessPeriodId = row.BusinessPeriodId, SourceVersion = row.SourceVersion,
        SourceCutoffAt = row.SourceCutoffAt, CashReceived = row.CashReceived,
        CashRefunded = row.CashRefunded, NetCash = row.NetCash, RetainedAmount = row.RetainedAmount,
        PendingFinanceCount = row.PendingFinanceCount, CorrectsSettlementId = row.CorrectsSettlementId,
        CorrectionVersion = row.CorrectionVersion
    };

    private static SettlementSummaryDto BuildFinalizedSummary(SettlementRunRow run, IReadOnlyList<SettlementResultLineRow> results)
        => new(run.GrossRevenue, run.DesignatedRevenueBase, run.DedicatedRoomGross, run.DedicatedRoomOwnerShare,
            run.DedicatedRoomCompanyRemainder, run.PublicRoomUnassignedRevenue, run.AdmissionRevenue,
            run.MealRevenue, run.CompanyRevenue, run.ServiceManagerPool, run.BackstagePool, run.CompanyIncome,
            run.ActivityNetRevenue, results.Sum(x => x.AfterRounding), results.Sum(x => x.CompanySubsidy), 0, 0,
            run.CashReceived, run.CashRefunded, run.NetCash, run.RetainedAmount, run.PendingFinanceCount);

    private static SettlementOverviewDto MapOverview(SettlementRunRow run, SettlementRuleRow rule,
        SettlementSourceData source, IReadOnlyList<SettlementResultLineRow> results, SettlementSummaryDto summary,
        IReadOnlyList<SettlementAnomalyDto>? anomalies = null)
        => new(MapRun(run), MapRule(rule), summary,
            source.StaffInputs.Select(x =>
            {
                var payableHours = run.DayType != "event" && x.Role == "backstage"
                    ? 0 : PayableHours(x.ActualMinutes, rule.TimeRoundMinutes);
                var backfill = source.AttendanceBackfillRequests
                    .Where(r => r.StaffId == x.StaffId && r.Role == x.Role)
                    .OrderByDescending(r => r.RequestedAt).FirstOrDefault();
                return new SettlementStaffInputDto(x.StaffId, x.DisplayName, x.RoleTitle, x.Role,
                    x.IsWorking, x.ActualMinutes, x.ActivityHours, payableHours,
                    IsPublicTipEligible(x, payableHours), x.IsBackstageParticipant, x.Note,
                    x.AttendanceSource, x.AttendanceRequestId ?? backfill?.Id, backfill?.Status,
                    backfill?.Reason, x.AttendanceApprovedBy, x.AttendanceApprovedAt, x.PayrollSnapshotVersion);
            }).ToArray(),
            results.Select(MapResult).ToArray(), anomalies ?? [],
             source.AttendanceBackfillRequests.Select(x => new SettlementAttendanceBackfillDto(x.Id, x.StaffId,
                 x.StaffName, x.Role, x.RequestedMinutes, x.Reason, x.Status, x.RequestedBy, x.RequestedAt,
                 x.ReviewedBy, x.ReviewedAt, x.ReviewNote)).ToArray(),
             new SettlementWorkflowDto(source.Period.BusinessPeriodId ?? source.Cash.BusinessPeriodId,
                 source.Period.PeriodStatus, source.Period.IntakeMode, source.Period.UnfinishedOrderCount,
                 source.Period.ActiveServiceCount, source.Cash.CashReceived, source.Cash.CashRefunded,
                 source.Cash.NetCash, source.Cash.RetainedAmount, source.Cash.PendingFinanceCount,
                 source.Period.PeriodStatus == "open",
                 source.Period.PeriodStatus == "open" && source.Period.UnfinishedOrderCount == 0,
                 source.Period.PeriodStatus == "closed" && run.Status != "finalized",
                 source.Cash.PendingFinanceCount > 0 || source.Period.UnfinishedOrderCount > 0,
                 source.PendingFinanceItems.Select(x => new SettlementPendingFinanceItemDto(x.SessionId,
                     x.CustomerName, x.OrderId, x.OrderNumber, x.PendingRecordCount)).ToArray()),
             source.PayrollAssignments.Select(x => new SettlementPayrollAssignmentDto(x.StaffId, x.DisplayName, x.PayrollRole,
                 x.PayrollSnapshotVersion, x.CanUseBackstage, x.StartedAt)).ToArray());

    private static SettlementRuleDto MapRule(SettlementRuleRow row) => new(row.Id, row.DayType,
        DateOnly.FromDateTime(row.EffectiveFrom), row.DesignatedHourlyRate, row.ServiceManagerHourlyRate,
        row.BackstageHourlyRate, row.DesignatedSharePercentage, row.PublicRoomStaffPercentage,
        row.DedicatedRoomOwnerPercentage, row.ServiceManagerPoolPercentage, row.BackstagePoolPercentage,
        row.CompanyPercentage, row.TimeRoundMinutes, 1, row.PublicTipMode);

    private static SettlementRunDto MapRun(SettlementRunRow row) => new(row.Id, DateOnly.FromDateTime(row.BusinessDate),
        row.SessionNo, row.DayType, row.Status, row.RuleVersionId, row.PublicTipAmount, row.AdmissionFeeOverride,
        row.AdmissionFeeSnapshot, row.ActivityExpense, row.CompanyShareHours, row.ActivityHoursConfirmed,
        row.FinalizedAt, row.BusinessPeriodId, row.SourceVersion, row.SourceCutoffAt, row.CashReceived,
        row.CashRefunded, row.NetCash, row.RetainedAmount, row.PendingFinanceCount,
        row.CorrectsSettlementId, row.CorrectionVersion, row.InputVersion, row.CalculationVersion);

    private static SettlementResultLineDto MapResult(SettlementResultLineRow row) => new(row.StaffId, row.DisplayName,
        row.Role, row.PayableHours, row.HourlyRate, row.BasePay, row.RevenueBase, row.RevenuePercentage,
        row.RevenueShare, row.DesignatedTip, row.PublicTip, row.PreferredPay, row.BeforeRounding,
        row.AfterRounding, row.CompanySubsidy, row.ManualAdjustment, row.AnomalyStatus, row.AnomalyNote);

    private static SaveStaffInputData MapInput(SaveSettlementStaffInputRequest input) => new()
    {
        PayrollSnapshotVersion = input.PayrollSnapshotVersion, StaffId = input.StaffId.Trim(), Role = input.Role, ActualMinutes = input.ActualMinutes,
        ActivityHours = input.ActivityHours, PublicTipEligible = input.PublicTipEligible,
        IsBackstageParticipant = input.IsBackstageParticipant, AttendanceSource = input.AttendanceSource,
        Note = input.Note?.Trim()
    };

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

    private static DateTimeOffset ToOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8));

    private static void EnsureEditable(SettlementRunRow run)
    {
        if (run.Status == "finalized") throw new BusinessException("正式結算已鎖定，請先重新開放新的營業時段。", "SETTLEMENT_LOCKED");
    }

    private static void ValidateDate(DateOnly date)
    {
        if (date == default) throw new BusinessException("營業日期不可為空。", "BUSINESS_DATE_REQUIRED");
    }

    private static void ValidateInputs(SaveSettlementInputsRequest request)
    {
        if (request.ActivityExpense < 0 || request.PublicTipAmount < 0)
            throw new BusinessException("活動費用與小費不可為負數。", "SETTLEMENT_AMOUNT_INVALID");
        if (request.StaffInputs.GroupBy(x => (x.StaffId, x.Role)).Any(x => x.Count() > 1))
            throw new BusinessException("同一人同一角色只能有一筆結算輸入。", "SETTLEMENT_INPUT_DUPLICATED");
    }

    private static void ValidateRule(SaveSettlementRuleRequest request)
    {
        if (request.ServiceManagerPoolPercentage + request.BackstagePoolPercentage + request.CompanyPercentage != 100)
            throw new BusinessException("服務生／經理、幕後與公司比例合計必須為 100%。", "SETTLEMENT_POOL_PERCENTAGE_INVALID");
        if (request.TimeRoundMinutes != 30)
            throw new BusinessException("目前工時計薪規則固定為半小時切點。", "SETTLEMENT_TIME_RULE_INVALID");
        if (request.MoneyRoundUnit != 1)
            throw new BusinessException("薪資金額最小支付單位固定為 1 Gil。", "SETTLEMENT_MONEY_RULE_INVALID");
    }

    private sealed record CalculationResult(SettlementRunRow Run, IReadOnlyList<SettlementResultLineRow> Results,
        SettlementSummaryDto Summary, IReadOnlyList<SettlementAnomalyDto> Anomalies)
    {
        public SaveCalculationData ToSave(SettlementRuleRow rule, IReadOnlyList<SettlementStaffInputRow> inputs) => new()
        {
            Run = Run, Results = Results,
            StaffInputs = inputs
        };
    }
}
