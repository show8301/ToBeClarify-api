using System.Text.Json;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Services.Ordering;

public static class OrderBackfillPolicy
{
    public static void Validate(OrderBackfillRequest request, OrderSessionRow session,
        BusinessPeriodRow? period, DateTime now)
    {
        if (!Guid.TryParseExact(request.OperationId, "D", out _) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500)
            throw new BusinessException("補登請填寫原因及有效操作編號。", "ORDER_BACKFILL_INPUT_INVALID");
        if (session.SessionStatus is not ("active" or "readonly") || period is null
            || period.Id != session.BusinessPeriodId || period.PeriodStatus is not ("open" or "closed" or "settled"))
            throw new BusinessException("此顧客沒有可補登的營業期。", "ORDER_BACKFILL_PERIOD_INVALID");
        ValidateTime(request.OccurredAt.ToOffset(TimeSpan.FromHours(8)).DateTime, period, now);
        if (request.ExpectedFinanceVersion < 0 || (request.LinkedCashRecordId is not null
            && (!Guid.TryParseExact(request.LinkedCashRecordId, "D", out _) || request.ExpectedCashRecordVersion is null or < 1)))
            throw new BusinessException("請重新讀取帳款後選擇既有收款。", "ORDER_BACKFILL_CASH_INVALID");
    }

    public static void ValidateTime(DateTime time, BusinessPeriodRow period, DateTime now)
    {
        var start = period.ActualOpenedAt ?? period.StartsAt;
        var end = period.ActualClosedAt ?? start.AddHours(48);
        if (time < start || time > end || time > start.AddHours(48) || time > now)
            throw new BusinessException("補登時間必須在原營業期內，且不可晚於現在。", "ORDER_BACKFILL_TIME_INVALID");
    }

    // Exact retries do not depend on current prices, generated IDs, quote expiry or account changes.
    public static string PayloadHash(SubmitOrderRequest request) => MenuPolicies.Hash(new {
        request.Meals, request.Nominations, request.Rooms, request.Tips, request.CustomerNote,
        request.CustomerLocation, request.IsManagerTransfer, request.Backfill
    });

    public static string DuplicateHash(SubmitOrderRequest request) => MenuPolicies.Hash(new {
        OccurredAt = request.Backfill!.OccurredAt.ToUniversalTime(),
        Meals = request.Meals.GroupBy(x => (x.Kind, ReferenceId: Canonical(x.ReferenceId), RecipientStaffId: Canonical(x.RecipientStaffId)))
            .Select(group => JsonSerializer.Serialize(new { group.Key.Kind, group.Key.ReferenceId,
                group.Key.RecipientStaffId, Quantity = group.Sum(x => x.Quantity) })).Order(StringComparer.Ordinal),
        Nominations = request.Nominations.Select(x => JsonSerializer.Serialize(new { StaffId = Canonical(x.StaffId), x.Mode, ServiceId = Canonical(x.ServiceId),
            x.SegmentCount, StartsAt = x.RequestedStartsAt.ToUniversalTime(), EndsAt = x.ActualEndsAt?.ToUniversalTime() })).Order(StringComparer.Ordinal),
        Rooms = request.Rooms.Select(x => JsonSerializer.Serialize(new { RoomId = Canonical(x.RoomId), x.SegmentCount,
            StartsAt = x.RequestedStartsAt.ToUniversalTime(), EndsAt = x.ActualEndsAt?.ToUniversalTime() })).Order(StringComparer.Ordinal),
        Tips = request.Tips.Select(x => JsonSerializer.Serialize(new { StaffId = Canonical(x.StaffId), x.Amount, x.StaffPercentage })).Order(StringComparer.Ordinal)
    });

    private static string? Canonical(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
