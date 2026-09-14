using System.Globalization;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Admin.Attendance;

public sealed class AttendanceRepository(AppDbContext dbContext)
{
    public async Task<StaffAttendanceOverviewDto> GetOverviewAsync(DateOnly businessDate, string? staffId,
        DateTimeOffset nowOffset, CancellationToken ct)
    {
        var now = nowOffset.ToOffset(TimeSpan.FromHours(8)).DateTime;
        await SyncScheduledAsync(businessDate, now, ct);
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var plans = (await connection.QueryAsync<PlanRow>(new CommandDefinition("""
            SELECT P.ID AS Id, P.STAFF_MEMBER_ID AS StaffId, M.DISPLAY_NAME AS DisplayName,
                   P.BUSINESS_DATE AS BusinessDate, P.START_TIME AS StartTime, P.END_TIME AS EndTime,
                   P.IS_WORKING AS IsWorking, P.APPROVAL_STATUS AS ApprovalStatus
            FROM STAFF_DUTY_PLANS P JOIN STAFF_MEMBERS M ON M.ID=P.STAFF_MEMBER_ID
            WHERE P.BUSINESS_DATE=@BusinessDate AND P.APPROVAL_STATUS='approved'
              AND P.IS_WORKING=TRUE AND (@StaffId IS NULL OR P.STAFF_MEMBER_ID=@StaffId)
            ORDER BY M.SORT_ORDER, M.DISPLAY_NAME;
            """, new { BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue), StaffId=staffId }, cancellationToken:ct))).ToArray();
        var events = (await connection.QueryAsync<EventRow>(new CommandDefinition("""
            SELECT E.ID AS Id, E.STAFF_MEMBER_ID AS StaffId, E.BUSINESS_DATE AS BusinessDate,
                   E.DUTY_PLAN_ID AS DutyPlanId, E.EVENT_TYPE AS EventType, E.EVENT_AT AS EventAt,
                   E.EVENT_END_AT AS EventEndAt, E.SOURCE AS Source, E.MINUTES_DELTA AS MinutesDelta,
                   E.BASE_EVENT_ID AS BaseEventId, E.OPERATION_ID AS OperationId, E.REASON AS Reason,
                   E.CREATED_AT AS CreatedAt
            FROM STAFF_ATTENDANCE_EVENTS E
            WHERE E.BUSINESS_DATE=@BusinessDate AND (@StaffId IS NULL OR E.STAFF_MEMBER_ID=@StaffId)
            ORDER BY E.STAFF_MEMBER_ID, E.EVENT_AT, E.CREATED_AT;
            """, new { BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue), StaffId=staffId }, cancellationToken:ct))).ToArray();
        var stops = (await connection.QueryAsync<StopRow>(new CommandDefinition("""
            SELECT STAFF_MEMBER_ID AS StaffId, COALESCE(STOP_ACCEPTING_NEW_ORDERS,FALSE) AS Stop
            FROM STAFF_DAILY_WORK_MODES WHERE BUSINESS_DATE=@BusinessDate
              AND (@StaffId IS NULL OR STAFF_MEMBER_ID=@StaffId);
            """, new { BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue), StaffId=staffId }, cancellationToken:ct))).ToDictionary(x=>x.StaffId, StringComparer.Ordinal);
        var active = (await connection.QueryAsync<ActiveRow>(new CommandDefinition("""
            SELECT N.STAFF_ID AS StaffId, COUNT(*) AS Count
            FROM ORDER_NOMINEES N JOIN ORDERS O ON O.ID=N.ORDER_ID
                 JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=O.SESSION_ID
            WHERE S.BUSINESS_DATE=@BusinessDate AND O.ORDER_STATUS IN ('confirmed','in_service')
              AND N.CONFIRMATION_STATUS IN ('confirmed','in_service') AND N.REQUESTED_SERVICE_ENDS_AT>@Now
            GROUP BY N.STAFF_ID;
            """, new { BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue), Now=now }, cancellationToken:ct))).ToDictionary(x=>x.StaffId, StringComparer.Ordinal);
        var summaries = plans.Select(plan => BuildSummary(plan, events.Where(x=>x.StaffId==plan.StaffId),
            stops.TryGetValue(plan.StaffId,out var stop) && stop.Stop, active.TryGetValue(plan.StaffId,out var busy) ? busy.Count : 0)).ToArray();
        return new StaffAttendanceOverviewDto(businessDate.ToString("yyyy-MM-dd"), nowOffset,
            summaries.Select(MapSummary).ToArray());
    }

    public async Task<StaffAttendanceEventDto> ApplyAsync(string staffId, StaffAttendanceActionRequest request,
        DateTimeOffset nowOffset, string actorId, CancellationToken ct)
    {
        var businessDate = DateOnly.ParseExact(request.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var now = nowOffset.ToOffset(TimeSpan.FromHours(8)).DateTime;
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var prior = await connection.QuerySingleOrDefaultAsync<EventRow>(new CommandDefinition("""
            SELECT ID AS Id, STAFF_MEMBER_ID AS StaffId, BUSINESS_DATE AS BusinessDate, DUTY_PLAN_ID AS DutyPlanId,
                   EVENT_TYPE AS EventType, EVENT_AT AS EventAt, EVENT_END_AT AS EventEndAt, SOURCE AS Source,
                   MINUTES_DELTA AS MinutesDelta, BASE_EVENT_ID AS BaseEventId, OPERATION_ID AS OperationId,
                   REASON AS Reason, CREATED_AT AS CreatedAt
            FROM STAFF_ATTENDANCE_EVENTS WHERE OPERATION_ID=@OperationId;
            """, new { request.OperationId }, tx, cancellationToken:ct));
        if (prior is not null) return MapEvent(prior);
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM STAFF_MEMBERS WHERE ID=@StaffId AND IS_ACTIVE=TRUE;", new {StaffId=staffId}, tx, cancellationToken:ct)) != 1)
            throw new BusinessException("找不到可出勤的店員。", "ATTENDANCE_STAFF_NOT_FOUND");
        var action = request.Action.Trim().ToLowerInvariant();
        var eventAt = (request.OccurredAt ?? nowOffset).ToOffset(TimeSpan.FromHours(8)).DateTime;
        var eventType = action; var source = "manual"; var delta = 0; DateTime? endAt = null; string? baseId = null;
        if (eventAt > now.AddMinutes(5)) throw new BusinessException("不能預先登記尚未發生的出勤。", "ATTENDANCE_TIME_FUTURE");
        if (action == "clock_out")
        {
            var open = await connection.QuerySingleOrDefaultAsync<EventRow>(new CommandDefinition("""
                SELECT E.ID AS Id, E.STAFF_MEMBER_ID AS StaffId, E.BUSINESS_DATE AS BusinessDate, E.DUTY_PLAN_ID AS DutyPlanId,
                       E.EVENT_TYPE AS EventType, E.EVENT_AT AS EventAt, E.EVENT_END_AT AS EventEndAt, E.SOURCE AS Source,
                       E.MINUTES_DELTA AS MinutesDelta, E.BASE_EVENT_ID AS BaseEventId, E.OPERATION_ID AS OperationId,
                       E.REASON AS Reason, E.CREATED_AT AS CreatedAt
                FROM STAFF_ATTENDANCE_EVENTS E
                WHERE E.STAFF_MEMBER_ID=@StaffId AND E.BUSINESS_DATE=@BusinessDate AND E.EVENT_TYPE='clock_in'
                  AND NOT EXISTS (SELECT 1 FROM STAFF_ATTENDANCE_EVENTS X WHERE X.BASE_EVENT_ID=E.ID)
                ORDER BY E.EVENT_AT DESC LIMIT 1 FOR UPDATE;
                """, new {StaffId=staffId, BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue)}, tx, cancellationToken:ct));
            if (open is null) throw new BusinessException("沒有可結束的進行中班次，請使用修改時間。", "ATTENDANCE_NO_OPEN_SHIFT");
            if (eventAt < open.EventAt) throw new BusinessException("下班時間不能早於上班時間。", "ATTENDANCE_TIME_RANGE_INVALID");
            baseId = open.Id; delta = Math.Max(0, (int)Math.Floor((eventAt - open.EventAt).TotalMinutes));
        }
        else if (action is "add_minutes" or "subtract_minutes")
        {
            if (request.Minutes is null or <= 0) throw new BusinessException("請填寫增加或減少的分鐘數。", "ATTENDANCE_MINUTES_REQUIRED");
            eventType = "adjustment"; delta = action == "add_minutes" ? request.Minutes.Value : -request.Minutes.Value;
        }
        else if (action == "set_times")
        {
            if (request.OccurredAt is null || request.EndedAt is null) throw new BusinessException("修改時間需要上下班時間。", "ATTENDANCE_TIMES_REQUIRED");
            endAt = request.EndedAt.Value.ToOffset(TimeSpan.FromHours(8)).DateTime;
            if (endAt <= eventAt) endAt = endAt.Value.AddDays(1);
            delta = Math.Max(0, (int)Math.Floor((endAt.Value - eventAt).TotalMinutes));
            eventType = "manual_interval";
        }
        else if (action is "stop_orders" or "resume_orders")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO STAFF_DAILY_WORK_MODES
                    (ID,STAFF_MEMBER_ID,BUSINESS_DATE,IS_WORKING,SCHEDULED_ROLES_JSON,ACTIVE_ROLES_JSON,
                     STOP_ACCEPTING_NEW_ORDERS,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
                SELECT @ModeId,@StaffId,@BusinessDate,COALESCE(D.IS_WORKING,TRUE),COALESCE(D.SCHEDULED_ROLES_JSON,'["service"]'),
                       COALESCE(D.ACTIVE_ROLES_JSON,'["service"]'),@Stop,@Now,@ActorId,@Now,@ActorId
                FROM STAFF_MEMBERS M LEFT JOIN STAFF_DAILY_WORK_MODES D
                  ON D.STAFF_MEMBER_ID=M.ID AND D.BUSINESS_DATE=@BusinessDate
                WHERE M.ID=@StaffId
                ON DUPLICATE KEY UPDATE STOP_ACCEPTING_NEW_ORDERS=@Stop,UPDATED_AT=@Now,UPDATED_BY=@ActorId;
                """, new {ModeId=Guid.NewGuid().ToString("D"), StaffId=staffId, BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue),
                    Stop=action=="stop_orders", Now=now, ActorId=actorId}, tx, cancellationToken:ct));
            eventType = action; source = "manual";
        }
        var id = Guid.NewGuid().ToString("D");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO STAFF_ATTENDANCE_EVENTS
                (ID,STAFF_MEMBER_ID,BUSINESS_DATE,EVENT_TYPE,EVENT_AT,EVENT_END_AT,SOURCE,MINUTES_DELTA,
                 BASE_EVENT_ID,OPERATION_ID,REASON,CREATED_AT,CREATED_BY)
            VALUES (@Id,@StaffId,@BusinessDate,@EventType,@EventAt,@EndAt,@Source,@Delta,@BaseId,@OperationId,@Reason,@Now,@ActorId);
            """, new {Id=id, StaffId=staffId, BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue), EventType=eventType,
                EventAt=eventAt, EndAt=endAt, Source=source, Delta=delta, BaseId=baseId, request.OperationId,
                Reason=string.IsNullOrWhiteSpace(request.Reason)?null:request.Reason.Trim(), Now=now, ActorId=actorId}, tx, cancellationToken:ct));
        await tx.CommitAsync(ct);
        return MapEvent(new EventRow { Id=id, StaffId=staffId, BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue),
            EventType=eventType, EventAt=eventAt, EventEndAt=endAt, Source=source, MinutesDelta=delta,
            BaseEventId=baseId, OperationId=request.OperationId, Reason=request.Reason, CreatedAt=now });
    }

    private async Task SyncScheduledAsync(DateOnly businessDate, DateTime now, CancellationToken ct)
    {
        await using var connection = await dbContext.CreateOpenConnectionAsync(ct);
        var plans = (await connection.QueryAsync<PlanRow>(new CommandDefinition("""
            SELECT P.ID AS Id,P.STAFF_MEMBER_ID AS StaffId,M.DISPLAY_NAME AS DisplayName,P.BUSINESS_DATE AS BusinessDate,
                   P.START_TIME AS StartTime,P.END_TIME AS EndTime,P.IS_WORKING AS IsWorking,P.APPROVAL_STATUS AS ApprovalStatus
            FROM STAFF_DUTY_PLANS P JOIN STAFF_MEMBERS M ON M.ID=P.STAFF_MEMBER_ID
            WHERE P.BUSINESS_DATE=@BusinessDate AND P.APPROVAL_STATUS='approved' AND P.IS_WORKING=TRUE;
            """, new {BusinessDate=businessDate.ToDateTime(TimeOnly.MinValue)}, cancellationToken:ct))).ToArray();
        foreach (var plan in plans)
        {
            var start = PlanTime(plan.BusinessDate, plan.StartTime);
            var end = PlanTime(plan.BusinessDate, plan.EndTime);
            if (start is null || end is null) continue;
            if (end <= start) end = end.Value.AddDays(1);
            var startOp = StableOp(plan.Id, "s");
            if (start <= now) await InsertScheduledAsync(connection, plan, start.Value, null, 0, startOp, now, ct);
            var active = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM ORDER_NOMINEES N JOIN ORDERS O ON O.ID=N.ORDER_ID JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=O.SESSION_ID
                WHERE S.BUSINESS_DATE=@BusinessDate AND N.STAFF_ID=@StaffId AND O.ORDER_STATUS IN ('confirmed','in_service')
                  AND N.CONFIRMATION_STATUS IN ('confirmed','in_service') AND N.REQUESTED_SERVICE_ENDS_AT>@Now;
                """, new {BusinessDate=plan.BusinessDate.Date, StaffId=plan.StaffId, Now=now}, cancellationToken:ct));
            if (end <= now && active > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO STAFF_DAILY_WORK_MODES
                        (ID,STAFF_MEMBER_ID,BUSINESS_DATE,IS_WORKING,SCHEDULED_ROLES_JSON,ACTIVE_ROLES_JSON,
                         STOP_ACCEPTING_NEW_ORDERS,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
                    SELECT @ModeId,@StaffId,@BusinessDate,COALESCE(D.IS_WORKING,TRUE),
                           COALESCE(D.SCHEDULED_ROLES_JSON,'[\"service\"]'),
                           COALESCE(D.ACTIVE_ROLES_JSON,'[\"service\"]'),TRUE,
                           @Now,'system:attendance',@Now,'system:attendance'
                    FROM STAFF_MEMBERS M
                    LEFT JOIN STAFF_DAILY_WORK_MODES D
                      ON D.STAFF_MEMBER_ID=M.ID AND D.BUSINESS_DATE=@BusinessDate
                    WHERE M.ID=@StaffId
                    ON DUPLICATE KEY UPDATE STOP_ACCEPTING_NEW_ORDERS=TRUE,UPDATED_AT=@Now,UPDATED_BY='system:attendance';
                    """, new {ModeId=Guid.NewGuid().ToString("D"), StaffId=plan.StaffId, BusinessDate=plan.BusinessDate.Date, Now=now}, cancellationToken:ct));
            }
            else if (end <= now)
                await InsertScheduledAsync(connection, plan, start.Value, end.Value, (int)Math.Floor((end.Value-start.Value).TotalMinutes), StableOp(plan.Id, "e"), now, ct);
        }
    }

    private static async Task InsertScheduledAsync(MySqlConnection connection, PlanRow plan, DateTime start, DateTime? end,
        int minutes, string operationId, DateTime now, CancellationToken ct)
        => await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO STAFF_ATTENDANCE_EVENTS
                (ID,STAFF_MEMBER_ID,BUSINESS_DATE,DUTY_PLAN_ID,EVENT_TYPE,EVENT_AT,EVENT_END_AT,SOURCE,MINUTES_DELTA,OPERATION_ID,REASON,CREATED_AT,CREATED_BY)
            VALUES (@Id,@StaffId,@BusinessDate,@PlanId,@Type,@EventAt,@EndAt,'scheduled',@Minutes,@OperationId,'核准班表自動出勤',@Now,'system:attendance')
            ON DUPLICATE KEY UPDATE ID=ID;
            """, new {Id=Guid.NewGuid().ToString("D"), StaffId=plan.StaffId, BusinessDate=plan.BusinessDate.Date,
                PlanId=plan.Id, Type=end is null ? "scheduled_start" : "scheduled_end", EventAt=end ?? start, EndAt=end,
                Minutes=minutes, OperationId=operationId, Now=now}, cancellationToken:ct));

    private static DateTime? PlanTime(DateTime date, TimeSpan? value)
        => value.HasValue ? date.Date.Add(value.Value) : null;

    private static string StableOp(string id, string suffix)
        => $"{id.Replace("-", "", StringComparison.Ordinal)[..Math.Min(34, id.Replace("-", "", StringComparison.Ordinal).Length)]}{suffix}";

    private static SummaryRow BuildSummary(PlanRow plan, IEnumerable<EventRow> source, bool stop, int active)
    {
        var rows = source.OrderBy(x=>x.EventAt).ToArray();
        var start = PlanTime(plan.BusinessDate, plan.StartTime); var end = PlanTime(plan.BusinessDate, plan.EndTime);
        if (start is not null && end is not null && end <= start) end = end.Value.AddDays(1);
        var actualStart = rows.Where(x=>x.EventType is "clock_in" or "manual_interval").Select(x=>x.EventAt).OrderBy(x=>x).FirstOrDefault();
        var actualEnd = rows.Where(x=>x.EventType is "clock_out" or "manual_interval").Select(x=>x.EventEndAt ?? x.EventAt).OrderByDescending(x=>x).FirstOrDefault();
        var worked = CalculateWorkedMinutes(rows);
        var adjustments = rows.Where(x=>x.EventType=="adjustment").Sum(x=>x.MinutesDelta);
        var open = rows.Any(x=>x.EventType=="clock_in" && !rows.Any(y=>y.BaseEventId==x.Id));
        return new SummaryRow(plan, start, end, actualStart == default ? null : actualStart, actualEnd == default ? null : actualEnd,
            end is null || start is null ? 0 : (int)Math.Floor((end.Value-start.Value).TotalMinutes), worked, adjustments,
            Math.Max(0, worked+adjustments), open, stop, active, rows);
    }

    private static StaffAttendanceSummaryDto MapSummary(SummaryRow x)
        => new(x.Plan.StaffId, x.Plan.DisplayName, x.Plan.BusinessDate.ToString("yyyy-MM-dd"), x.Plan.Id,
            x.ScheduledStart is { } ss ? Offset(ss) : null, x.ScheduledEnd is { } se ? Offset(se) : null,
            x.ActualStart is { } a ? Offset(a) : null, x.ActualEnd is { } e ? Offset(e) : null, x.ScheduledMinutes,
            x.WorkedMinutes, x.Adjustments, x.EffectiveMinutes, x.Open, x.Stop, x.Active,
            x.Events.Select(MapEvent).ToArray());

    private static StaffAttendanceEventDto MapEvent(EventRow x)
        => new(x.Id, x.StaffId, x.BusinessDate.ToString("yyyy-MM-dd"), x.DutyPlanId, x.EventType,
            Offset(x.EventAt), x.EventEndAt is { } end ? Offset(end) : null, x.Source, x.MinutesDelta,
            x.BaseEventId, x.OperationId, x.Reason, Offset(x.CreatedAt));

    private static int CalculateWorkedMinutes(IEnumerable<EventRow> source)
    {
        var rows = source.ToArray();
        var intervals = new List<(DateTime Start, DateTime End)>();
        var manual = rows.Where(x => x.EventType == "manual_interval" && x.EventEndAt is not null)
            .Select(x => (x.EventAt, x.EventEndAt!.Value)).ToArray();
        if (manual.Length > 0)
            intervals.AddRange(manual);
        else
        {
            var clockIns = rows.Where(x => x.EventType == "clock_in")
                .ToDictionary(x => x.Id, x => x.EventAt, StringComparer.Ordinal);
            intervals.AddRange(rows.Where(x => x.EventType == "clock_out" && x.BaseEventId is not null && clockIns.ContainsKey(x.BaseEventId))
                .Select(x => (clockIns[x.BaseEventId!], x.EventAt)));
            if (intervals.Count == 0)
                intervals.AddRange(rows.Where(x => x.EventType == "scheduled_end" && x.MinutesDelta > 0)
                    .Select(x => (x.EventAt.AddMinutes(-x.MinutesDelta), x.EventAt)));
        }
        var total = 0d;
        DateTime? mergedStart = null;
        DateTime? mergedEnd = null;
        foreach (var interval in intervals.Where(x => x.End > x.Start).OrderBy(x => x.Start).ThenBy(x => x.End))
        {
            if (mergedStart is null)
            {
                mergedStart = interval.Start;
                mergedEnd = interval.End;
            }
            else if (interval.Start <= mergedEnd)
                mergedEnd = interval.End > mergedEnd ? interval.End : mergedEnd;
            else
            {
                total += (mergedEnd!.Value - mergedStart.Value).TotalMinutes;
                mergedStart = interval.Start;
                mergedEnd = interval.End;
            }
        }
        if (mergedStart is not null) total += (mergedEnd!.Value - mergedStart.Value).TotalMinutes;
        return Math.Max(0, (int)Math.Floor(total));
    }

    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8));

    private sealed class PlanRow { public string Id {get;set;}=""; public string StaffId {get;set;}=""; public string DisplayName {get;set;}=""; public DateTime BusinessDate {get;set;} public TimeSpan? StartTime {get;set;} public TimeSpan? EndTime {get;set;} public bool IsWorking {get;set;} public string ApprovalStatus {get;set;}=""; }
    private sealed class EventRow { public string Id {get;set;}=""; public string StaffId {get;set;}=""; public DateTime BusinessDate {get;set;} public string? DutyPlanId {get;set;} public string EventType {get;set;}=""; public DateTime EventAt {get;set;} public DateTime? EventEndAt {get;set;} public string Source {get;set;}=""; public int MinutesDelta {get;set;} public string? BaseEventId {get;set;} public string OperationId {get;set;}=""; public string? Reason {get;set;} public DateTime CreatedAt {get;set;} }
    private sealed class StopRow { public string StaffId {get;set;}=""; public bool Stop {get;set;} }
    private sealed class ActiveRow { public string StaffId {get;set;}=""; public int Count {get;set;} }
    private sealed record SummaryRow(PlanRow Plan, DateTime? ScheduledStart, DateTime? ScheduledEnd, DateTime? ActualStart, DateTime? ActualEnd, int ScheduledMinutes, int WorkedMinutes, int Adjustments, int EffectiveMinutes, bool Open, bool Stop, int Active, EventRow[] Events);
}
