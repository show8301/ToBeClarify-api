using System.Security.Claims;
using System.Security.Cryptography;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Services.Client.Menu;
using ToBeClarify.Api.Services.Client.Shared;
using ToBeClarify.Api.Services.Client.Staff;

namespace ToBeClarify.Api.Services.Ordering;

public sealed class OrderingService : IOrderingService
{
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);
    private readonly IOrderingRepository _repository;
    private readonly IOrderingTokenService _tokens;
    private readonly IMenuService _menuService;
    private readonly IStaffService _staffService;
    private readonly IAppClock _clock;

    public OrderingService(IOrderingRepository repository, IOrderingTokenService tokens,
        IMenuService menuService, IStaffService staffService, IAppClock clock)
    {
        _repository = repository;
        _tokens = tokens;
        _menuService = menuService;
        _staffService = staffService;
        _clock = clock;
    }

    public async Task<OrderSessionIssuedDto> CreateSessionAsync(CreateOrderSessionRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var gameId = Required(request.GameId, "GAME_ID_REQUIRED");
        var day = DateOnly.FromDateTime(_clock.LocalDateTime);
        if (await _repository.GetSessionByGameIdAsync(gameId, day, cancellationToken) is not null)
            throw new BusinessException("此顧客今天已有點餐碼，請使用尋回功能。", "ORDER_SESSION_EXISTS");
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var id = NewId();
        var token = _tokens.Create(id, gameId, day);
        var recoveryCode = _tokens.CreateRecoveryCode();
        var now = _clock.LocalDateTime;
        var row = new OrderSessionRow
        {
            Id = id,
            GameId = gameId,
            CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? gameId : request.CustomerName.Trim(),
            BusinessDate = day.ToDateTime(TimeOnly.MinValue),
            AccessTokenHash = _tokens.Hash(token),
            RecoveryCodeHash = _tokens.Hash(recoveryCode),
            MaxNominatedStaff = request.MaxNominatedStaff ?? 1,
            PrepaidMealCredit = settings.MinimumMealCredit,
            RemainingMealCredit = settings.MinimumMealCredit,
            SessionStatus = "active",
            CreatedAt = now
        };
        await _repository.CreateSessionAsync(row, ActorId(actor), cancellationToken);
        return Issued(row, token, recoveryCode);
    }

    public async Task<OrderSessionIssuedDto> RotateSessionCredentialsAsync(string sessionId, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionByIdAsync(sessionId, cancellationToken);
        EnsureTodayAndActive(session);
        var token = _tokens.Create(session.Id, session.GameId, DateOnly.FromDateTime(session.BusinessDate));
        var recoveryCode = _tokens.CreateRecoveryCode();
        await _repository.RotateSessionCredentialsAsync(session.Id, _tokens.Hash(token), _tokens.Hash(recoveryCode),
            _clock.LocalDateTime, cancellationToken);
        return Issued(session, token, recoveryCode);
    }

    public async Task<OrderSessionDto> UpdateSessionAsync(string sessionId, UpdateOrderSessionRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        _ = await GetSessionByIdAsync(sessionId, cancellationToken);
        await _repository.UpdateSessionAsync(sessionId,
            string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName.Trim(),
            request.MaxNominatedStaff, request.RemainingMealCredit, ActorId(actor), ActorRole(actor),
            _clock.LocalDateTime, cancellationToken);
        return MapSession(await GetSessionByIdAsync(sessionId, cancellationToken));
    }

    public async Task<OrderSessionAccessDto> AccessSessionAsync(string token, CancellationToken cancellationToken)
    {
        var session = await ValidateTokenAsync(token, cancellationToken);
        await _repository.RotateSessionCredentialsAsync(session.Id, session.AccessTokenHash, null,
            _clock.LocalDateTime, cancellationToken);
        return new OrderSessionAccessDto(MapSession(session), MapSettings(await _repository.GetSettingsAsync(cancellationToken)));
    }

    public async Task<OrderSessionIssuedDto> RecoverSessionAsync(RecoverOrderSessionRequest request,
        CancellationToken cancellationToken)
    {
        var gameId = Required(request.GameId, "GAME_ID_REQUIRED");
        var day = DateOnly.FromDateTime(_clock.LocalDateTime);
        var session = await _repository.GetSessionByGameIdAsync(gameId, day, cancellationToken)
            ?? throw new BusinessException("找不到今天的點餐資料，請洽店員。", "ORDER_SESSION_NOT_FOUND");
        EnsureTodayAndActive(session);
        var expected = Convert.FromHexString(session.RecoveryCodeHash);
        var provided = Convert.FromHexString(_tokens.Hash(request.RecoveryCode));
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
            throw new BusinessException("店員協助碼不正確。", "ORDER_RECOVERY_CODE_INVALID");
        var token = _tokens.Create(session.Id, session.GameId, day);
        await _repository.RotateSessionCredentialsAsync(session.Id, _tokens.Hash(token), null,
            _clock.LocalDateTime, cancellationToken);
        return Issued(session, token, request.RecoveryCode);
    }

    public async Task<OrderCatalogDto> GetCatalogAsync(string token, CancellationToken cancellationToken)
    {
        _ = await ValidateTokenAsync(token, cancellationToken);
        var settingsTask = _repository.GetSettingsAsync(cancellationToken);
        var menuTask = _menuService.GetMenuAsync(cancellationToken);
        var staffTask = _staffService.GetStaffAsync(null, cancellationToken);
        await Task.WhenAll(settingsTask, menuTask, staffTask);
        return new OrderCatalogDto(MapSettings(await settingsTask), await menuTask, await staffTask);
    }

    public async Task<OrderDto> SubmitOrderAsync(string token, SubmitOrderRequest request,
        CancellationToken cancellationToken)
    {
        var session = await ValidateTokenAsync(token, cancellationToken);
        if (request.Meals.Count + request.Nominations.Count + request.Tips.Count == 0)
            throw new BusinessException("本次點餐尚未加入任何項目。", "ORDER_EMPTY");
        if (request.Nominations.Select(item => item.StaffId).Distinct(StringComparer.Ordinal).Count() > session.MaxNominatedStaff)
            throw new BusinessException($"本次最多可同時指名 {session.MaxNominatedStaff} 位店員。", "NOMINATION_LIMIT_EXCEEDED");
        if (request.Nominations.GroupBy(item => item.StaffId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new BusinessException("同一張訂單每位店員只能選擇一項服務；追加時數請另開新訂單。", "NOMINATION_STAFF_DUPLICATED");

        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var now = _clock.LocalDateTime;
        if (request.Nominations.Count > 0 && settings.NominationPausedUntil is { } pausedUntil && pausedUntil > now)
            throw new BusinessException("目前暫停受理指名服務，請稍後再試。", "NOMINATION_PAUSED");

        var orderId = NewId();
        var items = new List<NewOrderItem>();
        var nominees = new List<NewOrderNominee>();
        var tips = new List<NewOrderTip>();
        var sort = 0;
        var mealSubtotal = 0;

        foreach (var line in request.Meals)
        {
            var product = await _repository.GetMenuProductAsync(Required(line.ReferenceId, "MENU_REFERENCE_REQUIRED"),
                line.Kind, cancellationToken);
            if (product is null || !product.IsAvailable)
                throw new BusinessException("餐點已停售或不存在，請重新選擇。", "MENU_PRODUCT_UNAVAILABLE");
            var total = checked(product.Price * line.Quantity);
            mealSubtotal = checked(mealSubtotal + total);
            items.Add(new NewOrderItem(NewId(), line.Kind == "set" ? "menu_set" : "menu_item", product.Id,
                null, product.Name, product.Price, line.Quantity, null, null, total, "fixed", sort++));
        }

        foreach (var line in request.Nominations)
        {
            var offer = await _repository.GetStaffOfferAsync(Required(line.StaffId, "STAFF_ID_REQUIRED"),
                Required(line.ServiceId, "SERVICE_ID_REQUIRED"), cancellationToken);
            if (offer is null || !offer.IsWorkingToday || !offer.StaffIsNominatable ||
                !offer.ServiceIsNominatable || !offer.ServiceIsEnabled || !offer.Price.HasValue)
                throw new BusinessException("此店員或服務目前無法指名。", "NOMINATION_UNAVAILABLE");
            var startsAt = ToTaiwanDateTime(line.RequestedStartsAt);
            if (startsAt < now)
                throw new BusinessException("指名開始時間不可早於目前時間。", "NOMINATION_START_IN_PAST");
            var coveredMinutes = checked(line.SegmentCount * settings.SegmentMinutes);
            var serviceDuration = offer.DurationMinutes is > 0 ? offer.DurationMinutes.Value : coveredMinutes;
            var requiredSegments = (int)Math.Ceiling(serviceDuration / (double)settings.SegmentMinutes);
            if (line.SegmentCount < requiredSegments)
                throw new BusinessException($"{offer.ServiceName} 需要至少 {requiredSegments} 節，才能完整覆蓋 {serviceDuration} 分鐘。",
                    "NOMINATION_SEGMENTS_INSUFFICIENT");
            // Slots are sold in whole segments. A 30-minute service therefore reserves
            // the staff for the full 40-minute two-segment window before their buffer.
            var serviceEnds = startsAt.AddMinutes(coveredMinutes);
            var busyUntil = serviceEnds.AddMinutes(Math.Max(0, offer.BufferMinutes));
            if (await _repository.IsStaffBusyAsync(offer.StaffId, startsAt, busyUntil, cancellationToken))
                throw new BusinessException($"{offer.StaffName} 在所選時段已忙碌，請改選時段。", "STAFF_TIME_CONFLICT");

            var baseItemId = NewId();
            var baseTotal = checked(settings.BaseNominationFee * line.SegmentCount);
            items.Add(new NewOrderItem(baseItemId, "nomination_base", offer.StaffId, null,
                $"{offer.StaffName}｜基礎指名費", settings.BaseNominationFee, line.SegmentCount,
                line.SegmentCount, coveredMinutes, baseTotal, "per_segment", sort++));

            var perService = checked(offer.Price.Value + Math.Max(0, line.ParticipantCount - 1) * (offer.AdditionalPersonPrice ?? 0));
            var serviceTotal = offer.DurationMinutes is > 0 ? perService : checked(perService * line.SegmentCount);
            items.Add(new NewOrderItem(NewId(), "staff_service", offer.ServiceId, baseItemId,
                $"{offer.StaffName}｜{offer.ServiceName}", perService,
                offer.DurationMinutes is > 0 ? 1 : line.SegmentCount, line.SegmentCount, serviceDuration,
                serviceTotal, offer.DurationMinutes is > 0 ? "fixed_duration" : "per_segment", sort++));
            nominees.Add(new NewOrderNominee(NewId(), offer.StaffId, offer.StaffName, offer.ServiceId,
                offer.ServiceName, line.SegmentCount, coveredMinutes, startsAt, serviceEnds, busyUntil));
        }

        foreach (var line in request.Tips)
        {
            string? staffName = null;
            var staffPercentage = line.StaffPercentage;
            if (string.IsNullOrWhiteSpace(line.StaffId))
                staffPercentage = 0;
            else
                staffName = await _repository.GetStaffNameAsync(line.StaffId, cancellationToken)
                    ?? throw new BusinessException("找不到小費指定店員。", "TIP_STAFF_NOT_FOUND");
            var staffAmount = line.Amount * staffPercentage / 100;
            var storeAmount = line.Amount - staffAmount;
            var itemId = NewId();
            items.Add(new NewOrderItem(itemId, "tip", line.StaffId, null,
                staffName is null ? "店家小費" : $"小費｜{staffName}", line.Amount, 1, null, null,
                line.Amount, "tip_allocation", sort++));
            tips.Add(new NewOrderTip(NewId(), itemId, line.StaffId, staffName, line.Amount,
                staffPercentage, 100 - staffPercentage, staffAmount, storeAmount));
        }

        var subtotal = items.Sum(item => item.LineTotal);
        var creditApplied = Math.Min(session.RemainingMealCredit, mealSubtotal);
        var hasNomination = nominees.Count > 0;
        var aggregate = new NewOrderAggregate(orderId, session.Id, CreateOrderNumber(now),
            hasNomination ? "submitted" : "confirmed", hasNomination ? now : null, now,
            subtotal, creditApplied, subtotal - creditApplied,
            string.IsNullOrWhiteSpace(request.CustomerNote) ? null : request.CustomerNote.Trim(), items, nominees, tips);
        await _repository.CreateOrderAsync(aggregate, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string token, CancellationToken cancellationToken)
    {
        var session = await ValidateTokenAsync(token, cancellationToken);
        return await MapOrdersAsync(await _repository.GetOrdersBySessionAsync(session.Id, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<AdminOrderSessionDto>> GetAdminSessionsAsync(DateOnly? businessDate,
        string? search, CancellationToken cancellationToken)
    {
        var day = businessDate ?? DateOnly.FromDateTime(_clock.LocalDateTime);
        return (await _repository.GetAdminSessionsAsync(day, search, cancellationToken)).Select(row =>
            new AdminOrderSessionDto(MapSession(row), row.OrderCount, row.WaitingOrderCount,
                row.ConfirmedOrderCount, row.TotalAmount, ToOffset(row.LastOrderedAt))).ToArray();
    }

    public async Task<IReadOnlyList<OrderDto>> GetAdminOrdersAsync(string sessionId, CancellationToken cancellationToken)
    {
        _ = await GetSessionByIdAsync(sessionId, cancellationToken);
        return await MapOrdersAsync(await _repository.GetOrdersBySessionAsync(sessionId, cancellationToken), cancellationToken);
    }

    public async Task<OrderingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
        => MapSettings(await _repository.GetSettingsAsync(cancellationToken));

    public async Task<OrderingSettingsDto> SaveSettingsAsync(UpdateOrderingSettingsRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (!(request.ReminderAfterMinutes < request.EscalateAfterMinutes &&
              request.EscalateAfterMinutes < request.ExpireAfterMinutes))
            throw new BusinessException("提醒時間必須早於升級時間，升級時間必須早於失效時間。", "ORDER_TIMEOUT_SEQUENCE_INVALID");
        var row = new OrderingSettingsRow
        {
            MinimumMealCredit = request.MinimumMealCredit,
            BaseNominationFee = request.BaseNominationFee,
            SegmentMinutes = request.SegmentMinutes,
            ReminderAfterMinutes = request.ReminderAfterMinutes,
            EscalateAfterMinutes = request.EscalateAfterMinutes,
            ExpireAfterMinutes = request.ExpireAfterMinutes
        };
        await _repository.SaveSettingsAsync(row, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<OrderingSettingsDto> PauseNominationAsync(PauseNominationRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var until = request.Minutes == 0 ? (DateTime?)null : _clock.LocalDateTime.AddMinutes(request.Minutes);
        await _repository.SetNominationPauseAsync(until, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<OrderDto> ConfirmNomineeAsync(string orderId, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType)
            ?? throw new ForbiddenException("此帳號尚未連結店員，無法確認指名。", "STAFF_ACCOUNT_NOT_LINKED");
        await _repository.ConfirmNomineeAsync(orderId, staffId, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<OrderDto> RescheduleOrderAsync(string orderId, RescheduleOrderRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var startsAt = ToTaiwanDateTime(request.RequestedStartsAt);
        if (startsAt < _clock.LocalDateTime)
            throw new BusinessException("重新排程時間不可早於目前時間。", "NOMINATION_START_IN_PAST");
        await _repository.RescheduleOrderAsync(orderId, startsAt, ActorId(actor), ActorRole(actor),
            _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<OrderDto> UpdateOrderAsync(string orderId, UpdateAdminOrderRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        await _repository.UpdateOrderAsync(orderId, request.CustomerNote, request.InternalNote, request.Status,
            ActorId(actor), ActorRole(actor), _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<OrderDto> UpdateOrderItemAsync(string orderId, string itemId, UpdateOrderItemRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        await _repository.UpdateOrderItemAsync(orderId, itemId, request.Name, request.UnitPrice, request.Quantity,
            ActorId(actor), ActorRole(actor), _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public async Task<OrderDto> DeleteOrderItemAsync(string orderId, string itemId, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        await _repository.DeleteOrderItemAsync(orderId, itemId, ActorId(actor), ActorRole(actor),
            _clock.LocalDateTime, cancellationToken);
        return (await MapOrdersAsync(await _repository.GetOrderAsync(orderId, cancellationToken), cancellationToken)).Single();
    }

    public Task CancelOrderAsync(string orderId, OrderActionRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
        => _repository.CancelPendingOrderAsync(orderId, request.Reason, ActorId(actor), ActorRole(actor),
            _clock.LocalDateTime, cancellationToken);

    public async Task<IReadOnlyList<OrderingReportSummaryDto>> GetReportAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            throw new BusinessException("報表日期範圍無效或超過 366 天。", "REPORT_RANGE_INVALID");
        return (await _repository.GetReportAsync(from, to, cancellationToken)).Select(row =>
            new OrderingReportSummaryDto(row.BusinessDate, row.OrderCount, row.GrossAmount,
                row.MealCreditApplied, row.NetAmount, row.StaffTipAmount, row.StoreTipAmount)).ToArray();
    }

    public async Task<int> ExpireWaitingOrdersAsync(CancellationToken cancellationToken)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        return await _repository.ExpireWaitingOrdersAsync(_clock.LocalDateTime.AddMinutes(-settings.ExpireAfterMinutes),
            _clock.LocalDateTime, cancellationToken);
    }

    private async Task<OrderSessionRow> ValidateTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new BusinessException("缺少點餐碼。", "ORDER_TOKEN_REQUIRED");
        var payload = _tokens.Read(token.Trim());
        var today = DateOnly.FromDateTime(_clock.LocalDateTime);
        if (payload.BusinessDate != today)
            throw new BusinessException("點餐碼僅限開立當日使用。", "ORDER_TOKEN_EXPIRED");
        var session = await _repository.GetSessionByTokenHashAsync(_tokens.Hash(token.Trim()), cancellationToken)
            ?? throw new BusinessException("點餐碼已失效或已重新補發。", "ORDER_TOKEN_REVOKED");
        if (!string.Equals(session.Id, payload.SessionId, StringComparison.Ordinal) ||
            !string.Equals(session.GameId, payload.GameId, StringComparison.Ordinal))
            throw new BusinessException("點餐碼資料不一致。", "ORDER_TOKEN_MISMATCH");
        EnsureTodayAndActive(session);
        return session;
    }

    private async Task<OrderSessionRow> GetSessionByIdAsync(string sessionId, CancellationToken cancellationToken)
        => await _repository.GetSessionByIdAsync(Required(sessionId, "ORDER_SESSION_ID_REQUIRED"), cancellationToken)
            ?? throw new BusinessException("找不到顧客點餐資料。", "ORDER_SESSION_NOT_FOUND");

    private void EnsureTodayAndActive(OrderSessionRow session)
    {
        if (session.SessionStatus != "active")
            throw new BusinessException("此點餐碼已停用。", "ORDER_SESSION_INACTIVE");
        if (DateOnly.FromDateTime(session.BusinessDate) != DateOnly.FromDateTime(_clock.LocalDateTime))
            throw new BusinessException("點餐碼僅限開立當日使用。", "ORDER_TOKEN_EXPIRED");
    }

    private async Task<IReadOnlyList<OrderDto>> MapOrdersAsync(OrderBundle bundle, CancellationToken cancellationToken)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var now = _clock.LocalDateTime;
        return bundle.Orders.Select(order =>
        {
            var queueMinutes = order.QueueEnteredAt.HasValue
                ? Math.Max(0, (int)Math.Floor((now - order.QueueEnteredAt.Value).TotalMinutes)) : 0;
            var stage = QueueStage(order.OrderStatus, queueMinutes, settings);
            return new OrderDto(order.Id, order.OrderNumber, order.OrderStatus, stage, queueMinutes,
                ToOffset(order.SubmittedAt)!.Value, ToOffset(order.ConfirmedAt), order.Subtotal,
                order.MealCreditApplied, order.TotalAmount, order.CustomerNote, order.InternalNote,
                bundle.Items.Where(item => item.OrderId == order.Id).OrderBy(item => item.SortOrder).Select(item =>
                    new OrderItemDto(item.Id, item.ItemType, item.ReferenceId, item.ParentItemId,
                        item.NameSnapshot, item.UnitPrice, item.Quantity, item.SegmentCount,
                        item.DurationMinutes, item.LineTotal, item.PriceRule)).ToArray(),
                bundle.Nominees.Where(item => item.OrderId == order.Id).Select(item =>
                    new OrderNomineeDto(item.Id, item.StaffId, item.StaffNameSnapshot, item.ServiceId,
                        item.ServiceNameSnapshot, item.SegmentCount, item.ServiceDurationMinutes,
                        ToOffset(item.RequestedStartsAt)!.Value, ToOffset(item.RequestedServiceEndsAt)!.Value,
                        ToOffset(item.RequestedBusyUntil)!.Value, item.ConfirmationStatus,
                        ToOffset(item.ConfirmedAt))).ToArray(),
                bundle.Tips.Where(item => item.OrderId == order.Id).Select(item =>
                    new OrderTipDto(item.Id, item.StaffId, item.StaffNameSnapshot, item.TipAmount,
                        item.StaffPercentage, item.StorePercentage, item.StaffAmount, item.StoreAmount)).ToArray(),
                bundle.History.Where(item => item.OrderId == order.Id).Select(item =>
                    new OrderStatusHistoryDto(item.FromStatus ?? string.Empty, item.ToStatus, item.Reason,
                        item.ActorType, ToOffset(item.CreatedAt)!.Value)).ToArray());
        }).ToArray();
    }

    private static string QueueStage(string status, int minutes, OrderingSettingsRow settings)
        => status switch
        {
            "needs_reschedule" => "需重新排程",
            "expired" => "已失效",
            "confirmed" => "已成立",
            "in_service" => "服務中",
            "completed" => "已完成",
            "cancelled" => "已取消",
            "rejected" => "已退回",
            _ when minutes >= settings.ExpireAfterMinutes => "逾時",
            _ when minutes >= settings.EscalateAfterMinutes => "升級",
            _ when minutes >= settings.ReminderAfterMinutes => "提醒",
            _ => "等待確認"
        };

    private OrderingSettingsDto MapSettings(OrderingSettingsRow row)
        => new(row.MinimumMealCredit, row.BaseNominationFee, row.SegmentMinutes,
            row.ReminderAfterMinutes, row.EscalateAfterMinutes, row.ExpireAfterMinutes,
            ToOffset(row.NominationPausedUntil), row.NominationPausedUntil > _clock.LocalDateTime);

    private static OrderSessionDto MapSession(OrderSessionRow row)
        => new(row.Id, row.GameId, row.CustomerName, DateOnly.FromDateTime(row.BusinessDate),
            row.MaxNominatedStaff, row.PrepaidMealCredit, row.RemainingMealCredit, row.SessionStatus);

    private OrderSessionIssuedDto Issued(OrderSessionRow row, string token, string recoveryCode)
        => new(MapSession(row), token, _tokens.BuildOrderUrl(token), recoveryCode);

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

    private static string ActorRole(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.RoleClaimType) ?? "unknown";

    private static DateTime ToTaiwanDateTime(DateTimeOffset value)
        => value.ToOffset(TaiwanOffset).DateTime;

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value.HasValue ? ClientContentMappings.ToTaiwanOffset(value.Value) : null;

    private static string Required(string? value, string code)
        => string.IsNullOrWhiteSpace(value) ? throw new BusinessException("必要欄位未填。", code) : value.Trim();

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static string CreateOrderNumber(DateTime now)
        => $"{now:yyyyMMdd-HHmmss}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
}
