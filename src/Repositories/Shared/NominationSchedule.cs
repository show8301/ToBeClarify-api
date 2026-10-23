using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Repositories.Shared;

/// <summary>Shared by availability reads and transaction-time nomination validation.</summary>
public sealed class NominationSchedule
{
    public string StaffId { get; set; } = "";
    public bool IsAccepting { get; set; }
    public int BufferMinutes { get; set; }
    public DateTime? ShiftStartsAt { get; set; }
    public DateTime? ShiftEndsAt { get; set; }
    public List<NominationInterval> Occupied { get; set; } = [];
    public bool IsServing { get; set; }
    public bool IsOverdue { get; set; }

    public string Status(DateTime now, DateTime closeAt, int segmentMinutes)
    {
        if (ShiftStartsAt is not { } start || ShiftEndsAt is not { } end) return "off";
        if (end <= now) return "ended";
        if (!IsAccepting) return "paused";
        if (IsOverdue) return "overdue";
        var duration = TimeSpan.FromMinutes(segmentMinutes + BufferMinutes);
        var windows = FreeWindows(now, closeAt);
        if (!windows.Any(w => w.EndsAt - w.StartsAt >= duration)) return "fully_booked";
        if (now < start) return "before_shift";
        var current = windows.FirstOrDefault(w => w.StartsAt <= now && w.EndsAt > now);
        if (IsServing || current is null) return "busy";
        return current.EndsAt < (end < closeAt ? end : closeAt) ? "partially_reserved" : "available";
    }

    public IReadOnlyList<NominationInterval> FreeWindows(DateTime now, DateTime horizon)
    {
        if (!IsAccepting || IsOverdue || ShiftStartsAt is not { } shiftStart || ShiftEndsAt is not { } shiftEnd)
            return [];
        var cursor = shiftStart > now ? shiftStart : now;
        var end = shiftEnd < horizon ? shiftEnd : horizon;
        var free = new List<NominationInterval>();
        foreach (var block in Occupied.OrderBy(b => b.StartsAt))
        {
            if (block.EndsAt <= cursor || block.StartsAt >= end) continue;
            if (block.StartsAt > cursor) free.Add(new() { StartsAt = cursor, EndsAt = block.StartsAt });
            if (block.EndsAt > cursor) cursor = block.EndsAt;
            if (cursor >= end) break;
        }
        if (cursor < end) free.Add(new() { StartsAt = cursor, EndsAt = end });
        return free;
    }

    public void Validate(DateTime startsAt, DateTime busyUntil, bool requireAccepting = true)
    {
        if (requireAccepting && !IsAccepting)
            throw new ConflictException("店員班表或接單狀態已變更，請重新選擇。", "NOMINATION_UNAVAILABLE");
        if (ShiftStartsAt is not { } start || ShiftEndsAt is not { } end || startsAt < start || busyUntil > end || startsAt >= end || busyUntil <= startsAt)
            throw new ConflictException("此時段不在店員班次內，請選擇其他時間。", "NOMINATION_OUTSIDE_SHIFT");
        if (IsOverdue || Occupied.Any(b => b.EndsAt > startsAt && b.StartsAt < busyUntil))
            throw new ConflictException("此時段與店員既有預約或進行中服務重疊，請重新選擇時間。", "STAFF_TIME_CONFLICT");
    }

    public static async Task<IReadOnlyList<NominationSchedule>> ReadAsync(MySqlConnection connection,
        MySqlTransaction? tx, DateOnly day, DateTime now, CancellationToken ct,
        string? staffId = null, string? excludeNomineeId = null)
    {
        // Mutations use READ COMMITTED and lock the staff before entering here.
        // Lock plan/reservation sources; read running units without locking other
        // orders (their transitions may already hold their own order/unit lock).
        var locking = tx is null ? "" : " FOR UPDATE";
        var args = new { BusinessDate = day.ToDateTime(TimeOnly.MinValue), StaffId = staffId, ExcludeNomineeId = excludeNomineeId, Now = now };
        var schedules = (await connection.QueryAsync<NominationSchedule>(new CommandDefinition("""
            SELECT M.ID AS StaffId,
                (M.IS_ACTIVE=TRUE AND M.IS_NOMINATABLE=TRUE AND COALESCE(D.STOP_ACCEPTING_NEW_ORDERS,FALSE)=FALSE) AS IsAccepting,
                GREATEST(0,M.BUFFER_MINUTES) AS BufferMinutes,
                CASE WHEN P.IS_WORKING=TRUE AND P.APPROVAL_STATUS='approved' THEN TIMESTAMP(P.BUSINESS_DATE,P.START_TIME) END AS ShiftStartsAt,
                CASE WHEN P.IS_WORKING=TRUE AND P.APPROVAL_STATUS='approved' THEN TIMESTAMP(P.BUSINESS_DATE,P.END_TIME)
                    + INTERVAL (CASE WHEN P.END_TIME<=P.START_TIME THEN 1 ELSE 0 END) DAY END AS ShiftEndsAt
            FROM STAFF_MEMBERS M
            LEFT JOIN STAFF_DUTY_PLANS P ON P.STAFF_MEMBER_ID=M.ID AND P.BUSINESS_DATE=@BusinessDate
            LEFT JOIN STAFF_DAILY_WORK_MODES D ON D.STAFF_MEMBER_ID=M.ID AND D.BUSINESS_DATE=@BusinessDate
            WHERE M.IS_ACTIVE=TRUE AND (@StaffId IS NULL OR M.ID=@StaffId)
            """ + locking, args, tx, cancellationToken: ct))).AsList();
        var reservations = await connection.QueryAsync<NominationInterval>(new CommandDefinition("""
            SELECT STAFF_ID AS StaffId, STARTS_AT AS StartsAt, ENDS_AT AS EndsAt
            FROM STAFF_RESERVATIONS WHERE RESERVATION_STATUS='active' AND ENDS_AT>@Now
              AND (@StaffId IS NULL OR STAFF_ID=@StaffId)
            """ + locking, args, tx, cancellationToken: ct));
        var blocks = await connection.QueryAsync<NominationInterval>(new CommandDefinition("""
            SELECT STAFF_ID AS StaffId, STARTS_AT AS StartsAt, ENDS_AT AS EndsAt
            FROM STAFF_BUSY_BLOCKS WHERE BLOCK_STATUS='active' AND ENDS_AT>@Now
              AND (@StaffId IS NULL OR STAFF_ID=@StaffId)
              AND (@ExcludeNomineeId IS NULL OR ORDER_NOMINEE_ID<>@ExcludeNomineeId)
            """ + locking, args, tx, cancellationToken: ct));
        var running = (await connection.QueryAsync<NominationInterval>(new CommandDefinition("""
            SELECT F.STAFF_ID AS StaffId, COALESCE(F.ACTUAL_STARTS_AT,F.SCHEDULED_STARTS_AT) AS StartsAt,
                DATE_ADD(F.SCHEDULED_ENDS_AT,INTERVAL F.REST_MINUTES_RESERVED MINUTE) AS EndsAt
            FROM ORDER_FULFILLMENT_UNITS F
            WHERE F.KIND='nominee' AND F.STARTED_QUANTITY>F.COMPLETED_QUANTITY
              AND (@StaffId IS NULL OR F.STAFF_ID=@StaffId)
              AND (@ExcludeNomineeId IS NULL OR F.NOMINEE_ID<>@ExcludeNomineeId)
            """, args, tx, cancellationToken: ct))).AsList();
        // Legacy whole-order service has no fulfillment unit but must obey the same guard.
        running.AddRange(await connection.QueryAsync<NominationInterval>(new CommandDefinition("""
            SELECT N.STAFF_ID AS StaffId, COALESCE(O.STARTED_AT,N.REQUESTED_STARTS_AT) AS StartsAt, N.REQUESTED_BUSY_UNTIL AS EndsAt
            FROM ORDER_NOMINEES N JOIN ORDERS O ON O.ID=N.ORDER_ID
            WHERE O.FLOW_VERSION<2 AND O.ORDER_STATUS='in_service' AND N.CONFIRMATION_STATUS='confirmed'
              AND (@StaffId IS NULL OR N.STAFF_ID=@StaffId)
              AND (@ExcludeNomineeId IS NULL OR N.ID<>@ExcludeNomineeId)
            """, args, tx, cancellationToken: ct)));
        var occupied = reservations.Concat(blocks).ToLookup(b => b.StaffId, StringComparer.Ordinal);
        foreach (var schedule in schedules)
        {
            schedule.Occupied = occupied[schedule.StaffId].ToList();
            foreach (var service in running.Where(b => b.StaffId == schedule.StaffId))
            {
                schedule.IsServing = true;
                // An overdue service has no reliable release time. Do not promise another
                // slot until it is completed (or its schedule is explicitly adjusted).
                if (service.EndsAt <= now)
                {
                    schedule.IsOverdue = true;
                    service.EndsAt = schedule.ShiftEndsAt > now ? schedule.ShiftEndsAt.Value : now.AddDays(2);
                }
                schedule.Occupied.Add(service);
            }
        }
        return schedules;
    }
}

public sealed class NominationInterval
{
    public string StaffId { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
