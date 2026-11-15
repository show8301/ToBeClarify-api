using Dapper;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Repositories.Client.Calendar;

public sealed class BusinessCalendarRepository(AppDbContext db)
{
    public async Task<(IReadOnlyList<CalendarBusinessRow> Business, IReadOnlyList<CalendarShiftRow> Shifts)> GetAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        // Only explicitly saved plans and actual periods are announcements. Never synthesize
        // opening days from the default business-hours configuration.
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT D.BUSINESS_DATE BusinessDate, B.PERIOD_STATUS PeriodStatus,
                   COALESCE(B.ACTUAL_OPENED_AT, B.STARTS_AT, P.STARTS_AT) StartsAt,
                   COALESCE(B.ACTUAL_CLOSED_AT, B.PROJECTED_CLOSE_AT, B.ENDS_AT, P.ENDS_AT) EndsAt
            FROM (
                SELECT BUSINESS_DATE FROM BUSINESS_DAY_PLANS WHERE BUSINESS_DATE BETWEEN @FromDate AND @ToDate
                UNION
                SELECT BUSINESS_DATE FROM BUSINESS_PERIODS WHERE BUSINESS_DATE BETWEEN @FromDate AND @ToDate
            ) D
            LEFT JOIN BUSINESS_DAY_PLANS P ON P.BUSINESS_DATE = D.BUSINESS_DATE
            LEFT JOIN BUSINESS_PERIODS B ON B.BUSINESS_DATE = D.BUSINESS_DATE;

            SELECT P.BUSINESS_DATE BusinessDate, M.ID StaffId, M.DISPLAY_NAME DisplayName,
                   M.AVATAR_MEDIA_ID AvatarMediaId,
                   TIMESTAMP(P.BUSINESS_DATE, P.START_TIME) StartsAt,
                   TIMESTAMP(P.BUSINESS_DATE, P.END_TIME) + INTERVAL
                       (CASE WHEN P.END_TIME <= P.START_TIME THEN 1 ELSE 0 END) DAY EndsAt
            FROM STAFF_DUTY_PLANS P
            JOIN STAFF_MEMBERS M ON M.ID = P.STAFF_MEMBER_ID
            WHERE P.BUSINESS_DATE BETWEEN @FromDate AND @ToDate
              AND P.APPROVAL_STATUS = 'approved' AND P.IS_WORKING = TRUE
              AND M.IS_ACTIVE = TRUE AND P.START_TIME IS NOT NULL AND P.END_TIME IS NOT NULL
            ORDER BY P.BUSINESS_DATE, P.START_TIME, M.SORT_ORDER, M.DISPLAY_NAME, M.ID;
            """, new { FromDate = from.ToDateTime(TimeOnly.MinValue), ToDate = to.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct));
        return ((await results.ReadAsync<CalendarBusinessRow>()).ToArray(),
            (await results.ReadAsync<CalendarShiftRow>()).ToArray());
    }
}

public sealed class CalendarBusinessRow
{
    public DateTime BusinessDate { get; set; }
    public string? PeriodStatus { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}

public sealed class CalendarShiftRow
{
    public DateTime BusinessDate { get; set; }
    public string StaffId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarMediaId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
