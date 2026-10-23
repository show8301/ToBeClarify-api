using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    public async Task<IReadOnlyList<StaffNominationAvailabilityDto>> GetNominationAvailabilityAsync(
        DateOnly day, DateTime now, DateTime horizon, CancellationToken ct)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        var schedules = await NominationSchedule.ReadAsync(connection, null, day, now, ct);
        static DateTimeOffset At(DateTime value) => new(value, TimeSpan.FromHours(8));
        return schedules.Select(s => new StaffNominationAvailabilityDto(s.StaffId, s.IsAccepting,
            s.ShiftStartsAt is { } start ? At(start) : null, s.ShiftEndsAt is { } end ? At(end) : null,
            s.BufferMinutes, s.IsServing, s.IsOverdue,
            s.FreeWindows(now, horizon).Select(w => new NominationWindowDto(At(w.StartsAt), At(w.EndsAt))).ToArray())).ToArray();
    }

    private static async Task ValidateNominationScheduleAsync(MySqlConnection connection, MySqlTransaction tx,
        string staffId, string sessionId, DateTime startsAt, DateTime busyUntil, DateTime now,
        CancellationToken ct, string? excludeNomineeId = null, bool requireAccepting = true,
        string? fulfillmentPeriodId = null, bool starting = false)
    {
        await LockStaffRowsAsync(connection, tx, [staffId], ct);
        var day = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            fulfillmentPeriodId is null
                ? "SELECT BUSINESS_DATE FROM CUSTOMER_ORDER_SESSIONS WHERE ID=@SessionId;"
                : "SELECT BUSINESS_DATE FROM BUSINESS_PERIODS WHERE ID=@PeriodId;",
            new { SessionId = sessionId, PeriodId = fulfillmentPeriodId }, tx, cancellationToken: ct));
        var schedule = (await NominationSchedule.ReadAsync(connection, tx, DateOnly.FromDateTime(day), now,
            ct, staffId, excludeNomineeId)).SingleOrDefault()
            ?? throw new ConflictException("店員班表或接單狀態已變更，請重新選擇。", "NOMINATION_UNAVAILABLE");
        if (starting && schedule.ShiftStartsAt is { } shiftStart && startsAt < shiftStart)
            throw new ConflictException("尚未到店員上班時間，無法開始服務。", "NOMINATION_SHIFT_NOT_STARTED");
        if (starting && schedule.IsServing)
            throw new ConflictException("此店員仍有服務進行中，請先確認並處理目前服務。", "STAFF_ALREADY_IN_SERVICE");
        schedule.Validate(startsAt, busyUntil, requireAccepting);
    }
}
