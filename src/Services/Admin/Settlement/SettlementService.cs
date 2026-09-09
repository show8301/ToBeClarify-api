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

public sealed class SettlementService : ISettlementService
{
    private static readonly string[] InputRoles = ["designated", "service", "manager", "backstage"];
    private readonly ISettlementRepository _repository;
    private readonly IAppClock _clock;

    public SettlementService(ISettlementRepository repository, IAppClock clock)
    {
        _repository = repository;
        _clock = clock;
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
        if (run.Status == "finalized" && source.Results.Count > 0)
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
        await _repository.SaveCalculationAsync(run.Id, calculation.ToSave(rule), ActorId(actor), _clock.LocalDateTime,
            cancellationToken);
        return await GetOverviewAsync(request.BusinessDate, request.SessionNo, cancellationToken);
    }

    public async Task<SettlementOverviewDto> FinalizeAsync(SettlementFinalizeRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var calculated = await CalculateAsync(request, actor, cancellationToken);
        if (calculated.Anomalies.Count > 0)
            throw new BusinessException("結算仍有需手動處理的異常，完成處理前不可正式結算。", "SETTLEMENT_REQUIRES_MANUAL_HANDLING");
        await _repository.FinalizeAsync(calculated.Run.Id, ActorId(actor), _clock.LocalDateTime, cancellationToken);
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
        var anomalies = new List<SettlementAnomalyDto>();
        var orderMap = source.Orders.ToDictionary(x => x.OrderId, StringComparer.Ordinal);
        var itemMap = source.Items.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var nomineeMap = source.Nominees.GroupBy(x => x.OrderId).ToDictionary(x => x.Key,
            x => x.Select(n => n.StaffId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var staffNames = source.Nominees.GroupBy(x => x.StaffId).ToDictionary(x => x.Key, x => x.First().StaffName,
            StringComparer.Ordinal);
        foreach (var input in source.StaffInputs) staffNames[input.StaffId] = input.DisplayName;
        var designatedBase = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var designatedShare = new Dictionary<string, decimal>(StringComparer.Ordinal);
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
        decimal AmountFor(string orderId, int rawAmount)
        {
            if (!orderMap.TryGetValue(orderId, out var order)) return rawAmount;
            var collected = order.AdjustedAmount ?? order.TotalAmount;
            var revenueCollected = Math.Max(0, collected - order.TipAmount);
            var revenueSubtotal = Math.Max(0, order.Subtotal - order.TipAmount);
            return revenueSubtotal <= 0 ? 0 : rawAmount * (decimal)revenueCollected / revenueSubtotal;
        }

        void AddDesignated(string staffId, decimal amount, decimal percentage)
        {
            if (string.IsNullOrWhiteSpace(staffId)) return;
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
                var amount = AmountFor(item.OrderId, item.LineTotal);
                if (item.ItemType == "nomination_base")
                {
                    AddDesignated(item.ReferenceId ?? string.Empty, amount, rule.DesignatedSharePercentage);
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
                        AddDesignated(staffId, amount, rule.DesignatedSharePercentage);
                        directCompany += amount * (100m - rule.DesignatedSharePercentage) / 100m;
                    }
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
            AddDesignated(addon.StaffId, amount, rule.DesignatedSharePercentage);
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
                AddDesignated(nominees[0], amount, rule.PublicRoomStaffPercentage);
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
        var admissionFee = run.AdmissionFeeOverride ?? ParseMoney(admission?.AdmissionFeeText);
        var admissionRevenue = (admission?.SessionCount ?? 0) * admissionFee;
        var designatedRevenueBase = designatedBase.Values.Sum();
        var companyRevenue = directCompany + dedicatedCompanyRemainder + publicRoomUnassigned + admissionRevenue + mealRevenue + unknownRevenue;
        var servicePool = companyRevenue * rule.ServiceManagerPoolPercentage / 100m;
        var backstagePool = companyRevenue * rule.BackstagePoolPercentage / 100m;
        var companyIncome = companyRevenue * rule.CompanyPercentage / 100m;
        var grossRevenue = designatedRevenueBase + dedicatedGross + publicRoomUnassigned + admissionRevenue + mealRevenue + unknownRevenue;
        var runCopy = CopyRun(run);
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

        var results = run.DayType == "event"
            ? CalculateEventResults(runCopy, source, rule, grossRevenue, anomalies)
            : CalculateNormalResults(runCopy, source, rule, designatedBase, designatedShare, designatedTips,
                servicePool, backstagePool, dedicatedShareByOwner, staffNames, anomalies);
        runCopy.ActivityNetRevenue = run.DayType == "event" ? grossRevenue - run.ActivityExpense : 0;
        if (run.DayType == "event")
        {
            var activityHours = source.StaffInputs.Where(x => x.ActivityHours is > 0).Sum(x => x.ActivityHours!.Value);
            var activityDenominator = activityHours + (run.CompanyShareHours ?? 0);
            runCopy.CompanyRevenue = 0;
            runCopy.ServiceManagerPool = 0;
            runCopy.BackstagePool = 0;
            runCopy.CompanyIncome = activityDenominator <= 0 ? 0 : runCopy.ActivityNetRevenue * (run.CompanyShareHours ?? 0) / activityDenominator;
        }
        var summary = new SettlementSummaryDto(runCopy.GrossRevenue, runCopy.DesignatedRevenueBase,
            runCopy.DedicatedRoomGross, runCopy.DedicatedRoomOwnerShare, runCopy.DedicatedRoomCompanyRemainder,
            runCopy.PublicRoomUnassignedRevenue, runCopy.AdmissionRevenue, runCopy.MealRevenue, runCopy.CompanyRevenue,
            runCopy.ServiceManagerPool, runCopy.BackstagePool, runCopy.CompanyIncome, runCopy.ActivityNetRevenue,
            results.Sum(x => x.AfterRounding), results.Sum(x => x.CompanySubsidy));
        return new CalculationResult(runCopy, results, summary, anomalies);
    }

    private static IReadOnlyList<SettlementResultLineRow> CalculateNormalResults(SettlementRunRow run,
        SettlementSourceData source, SettlementRuleRow rule, IReadOnlyDictionary<string, decimal> designatedBase,
        IReadOnlyDictionary<string, decimal> designatedShare, IReadOnlyDictionary<string, decimal> designatedTips,
        decimal servicePool, decimal backstagePool, IReadOnlyDictionary<string, decimal> dedicatedShareByOwner,
        IReadOnlyDictionary<string, string> staffNames, List<SettlementAnomalyDto> anomalies)
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

        foreach (var staffId in designatedBase.Keys.Concat(designatedTips.Keys).Distinct(StringComparer.Ordinal))
        {
            var input = inputs.FirstOrDefault(x => x.StaffId == staffId && x.Role == "designated");
            if (input is null) anomalies.Add(new SettlementAnomalyDto("DESIGNATED_INPUT_MISSING", "有指名營業額但沒有指名人員工時輸入。", staffId));
            var hours = input is null ? 0 : payable.GetValueOrDefault((staffId, "designated"));
            AddResult(results, run, staffId, "designated", hours, rule.DesignatedHourlyRate,
                designatedBase.GetValueOrDefault(staffId), rule.DesignatedSharePercentage,
                designatedShare.GetValueOrDefault(staffId), designatedTips.GetValueOrDefault(staffId),
                PublicTipFor(input, hours, publicTips, tipHours), 1, staffNames.GetValueOrDefault(staffId));
        }
        foreach (var input in inputs.Where(x => x.Role is "service" or "manager"))
        {
            var hours = payable.GetValueOrDefault((input.StaffId, input.Role));
            var share = serviceManagerHours <= 0 ? 0 : servicePool * hours / serviceManagerHours;
            AddResult(results, run, input.StaffId, input.Role, hours, rule.ServiceManagerHourlyRate, 0,
                rule.ServiceManagerPoolPercentage, share, 0, PublicTipFor(input, hours, publicTips, tipHours),
                1, staffNames.GetValueOrDefault(input.StaffId));
        }
        if (backstage.Length > 0)
        {
            foreach (var input in backstage)
                AddResult(results, run, input.StaffId, "backstage", 0, rule.BackstageHourlyRate, 0,
                    rule.BackstagePoolPercentage, backstagePool / backstage.Length, 0, 0, 1,
                    staffNames.GetValueOrDefault(input.StaffId));
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
                var hours = payable.GetValueOrDefault((input.StaffId, input.Role));
                var activityShare = totalHours <= 0 ? 0 : (grossRevenue - run.ActivityExpense)
                    * activityHours[(input.StaffId, input.Role)] / totalHours;
                var hourlyRate = input.Role is "service" or "manager"
                    ? rule.ServiceManagerHourlyRate
                    : input.Role == "designated" ? rule.DesignatedHourlyRate : rule.BackstageHourlyRate;
                var publicTip = PublicTipFor(input, hours, run.PublicTipAmount, publicTipHours);
                return MakeResult(run, input.StaffId, input.Role, hours, hourlyRate, activityHours[(input.StaffId, input.Role)],
                    100, activityShare, designatedTips.GetValueOrDefault(input.StaffId), publicTip, 1,
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
        var preferred = role == "dedicated_room_owner" ? revenueShare : Math.Max(basePay, revenueShare);
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
        Status = row.Status, RuleVersionId = row.RuleVersionId, RuleSnapshotJson = row.RuleSnapshotJson,
        PublicTipAmount = row.PublicTipAmount, AdmissionFeeOverride = row.AdmissionFeeOverride,
        AdmissionFeeSnapshot = row.AdmissionFeeSnapshot, ActivityExpense = row.ActivityExpense,
        CompanyShareHours = row.CompanyShareHours, ActivityHoursConfirmed = row.ActivityHoursConfirmed
    };

    private static SettlementSummaryDto BuildFinalizedSummary(SettlementRunRow run, IReadOnlyList<SettlementResultLineRow> results)
        => new(run.GrossRevenue, run.DesignatedRevenueBase, run.DedicatedRoomGross, run.DedicatedRoomOwnerShare,
            run.DedicatedRoomCompanyRemainder, run.PublicRoomUnassignedRevenue, run.AdmissionRevenue,
            run.MealRevenue, run.CompanyRevenue, run.ServiceManagerPool, run.BackstagePool, run.CompanyIncome,
            run.ActivityNetRevenue, results.Sum(x => x.AfterRounding), results.Sum(x => x.CompanySubsidy));

    private static SettlementOverviewDto MapOverview(SettlementRunRow run, SettlementRuleRow rule,
        SettlementSourceData source, IReadOnlyList<SettlementResultLineRow> results, SettlementSummaryDto summary,
        IReadOnlyList<SettlementAnomalyDto>? anomalies = null)
        => new(MapRun(run), MapRule(rule), summary,
            source.StaffInputs.Select(x =>
            {
                var payableHours = PayableHours(x.ActualMinutes, rule.TimeRoundMinutes);
                var backfill = source.AttendanceBackfillRequests
                    .Where(r => r.StaffId == x.StaffId && r.Role == x.Role)
                    .OrderByDescending(r => r.RequestedAt).FirstOrDefault();
                return new SettlementStaffInputDto(x.StaffId, x.DisplayName, x.RoleTitle, x.Role,
                    x.IsWorking, x.ActualMinutes, x.ActivityHours, payableHours,
                    IsPublicTipEligible(x, payableHours), x.IsBackstageParticipant, x.Note,
                    x.AttendanceSource, x.AttendanceRequestId ?? backfill?.Id, backfill?.Status,
                    backfill?.Reason, x.AttendanceApprovedBy, x.AttendanceApprovedAt);
            }).ToArray(),
            results.Select(MapResult).ToArray(), anomalies ?? [],
            source.AttendanceBackfillRequests.Select(x => new SettlementAttendanceBackfillDto(x.Id, x.StaffId,
                x.StaffName, x.Role, x.RequestedMinutes, x.Reason, x.Status, x.RequestedBy, x.RequestedAt,
                x.ReviewedBy, x.ReviewedAt, x.ReviewNote)).ToArray());

    private static SettlementRuleDto MapRule(SettlementRuleRow row) => new(row.Id, row.DayType,
        DateOnly.FromDateTime(row.EffectiveFrom), row.DesignatedHourlyRate, row.ServiceManagerHourlyRate,
        row.BackstageHourlyRate, row.DesignatedSharePercentage, row.PublicRoomStaffPercentage,
        row.DedicatedRoomOwnerPercentage, row.ServiceManagerPoolPercentage, row.BackstagePoolPercentage,
        row.CompanyPercentage, row.TimeRoundMinutes, 1, row.PublicTipMode);

    private static SettlementRunDto MapRun(SettlementRunRow row) => new(row.Id, DateOnly.FromDateTime(row.BusinessDate),
        row.SessionNo, row.DayType, row.Status, row.RuleVersionId, row.PublicTipAmount, row.AdmissionFeeOverride,
        row.AdmissionFeeSnapshot, row.ActivityExpense, row.CompanyShareHours, row.ActivityHoursConfirmed,
        row.FinalizedAt);

    private static SettlementResultLineDto MapResult(SettlementResultLineRow row) => new(row.StaffId, row.DisplayName,
        row.Role, row.PayableHours, row.HourlyRate, row.BasePay, row.RevenueBase, row.RevenuePercentage,
        row.RevenueShare, row.DesignatedTip, row.PublicTip, row.PreferredPay, row.BeforeRounding,
        row.AfterRounding, row.CompanySubsidy, row.ManualAdjustment, row.AnomalyStatus, row.AnomalyNote);

    private static SaveStaffInputData MapInput(SaveSettlementStaffInputRequest input) => new()
    {
        StaffId = input.StaffId.Trim(), Role = input.Role, ActualMinutes = input.ActualMinutes,
        ActivityHours = input.ActivityHours, PublicTipEligible = input.PublicTipEligible,
        IsBackstageParticipant = input.IsBackstageParticipant, AttendanceSource = input.AttendanceSource,
        Note = input.Note?.Trim()
    };

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

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
        public SaveCalculationData ToSave(SettlementRuleRow rule) => new() { Run = Run, Results = Results };
    }
}
