using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Repositories.Shared;

public sealed class PayrollSnapshotRow
{
    public string StaffId { get; set; } = "";
    public string? DutyPlanId { get; set; }
    public string? PayrollRole { get; set; }
    public bool CanUseBackstage { get; set; }
    public bool BackstageParticipant { get; set; }
    public DateTime StartedAt { get; set; }
    public int Version { get; set; }
}

public static class PayrollSnapshotStore
{
    public static async Task<PayrollSnapshotRow?> ReadAsync(MySqlConnection connection, MySqlTransaction? tx,
        string staffId, DateTime date, CancellationToken ct)
        => await connection.QuerySingleOrDefaultAsync<PayrollSnapshotRow>(new CommandDefinition("""
            SELECT CAST(STAFF_MEMBER_ID AS CHAR(36)) AS StaffId,
                   CAST(DUTY_PLAN_ID AS CHAR(36)) AS DutyPlanId, PAYROLL_ROLE AS PayrollRole,
                   CAN_USE_BACKSTAGE AS CanUseBackstage, BACKSTAGE_PARTICIPANT AS BackstageParticipant,
                   STARTED_AT AS StartedAt, VERSION AS Version
            FROM STAFF_PAYROLL_SNAPSHOTS WHERE STAFF_MEMBER_ID=@StaffId AND BUSINESS_DATE=@Date
            FOR UPDATE;
            """, new { StaffId = staffId, Date = date.Date }, tx, cancellationToken: ct));

    // Caller holds the staff row before the plan/snapshot locks, just as duty-plan writes do.
    public static async Task<PayrollSnapshotRow?> CaptureAsync(MySqlConnection connection, MySqlTransaction tx,
        string staffId, DateTime date, DateTime startedAt, string actorId, DateTime now, bool required, CancellationToken ct)
    {
        var existing = await ReadAsync(connection, tx, staffId, date, ct);
        if (existing is not null)
        {
            if (required && existing.PayrollRole is null) throw Unresolved();
            return existing;
        }
        var plan = await connection.QuerySingleOrDefaultAsync<Plan>(new CommandDefinition("""
            SELECT P.ID, P.PAYROLL_ROLE AS PayrollRole,
                   JSON_CONTAINS(P.SCHEDULED_ROLES_JSON, '"backstage"') AS BackstageParticipant,
                   EXISTS(SELECT 1 FROM ADMIN_USERS U WHERE U.STAFF_MEMBER_ID=P.STAFF_MEMBER_ID
                     AND U.IS_ACTIVE=TRUE AND U.ROLE_LEVEL='developer') AS CanUseBackstage,
                   EXISTS(SELECT 1 FROM ADMIN_USERS U WHERE U.STAFF_MEMBER_ID=P.STAFF_MEMBER_ID
                     AND U.IS_ACTIVE=TRUE AND U.ROLE_LEVEL IN ('manager','developer')) AS CanUseManager
            FROM STAFF_DUTY_PLANS P WHERE P.STAFF_MEMBER_ID=@StaffId AND P.BUSINESS_DATE=@Date
              AND P.IS_WORKING=TRUE AND P.APPROVAL_STATUS='approved' FOR UPDATE;
            """, new { StaffId = staffId, Date = date.Date }, tx, cancellationToken: ct));
        var valid = plan?.PayrollRole is "service" or "designated"
            || plan?.PayrollRole == "manager" && plan.CanUseManager
            || plan?.PayrollRole == "backstage" && plan.CanUseBackstage;
        if (!valid)
        {
            if (required) throw Unresolved();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO STAFF_PAYROLL_SNAPSHOTS (STAFF_MEMBER_ID,BUSINESS_DATE,DUTY_PLAN_ID,STARTED_AT)
                VALUES (@StaffId,@Date,@PlanId,@StartedAt);
                """, new { StaffId = staffId, Date = date.Date, PlanId = plan?.Id, StartedAt = startedAt }, tx, cancellationToken: ct));
            return await ReadAsync(connection, tx, staffId, date, ct);
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO STAFF_PAYROLL_SNAPSHOTS
              (STAFF_MEMBER_ID,BUSINESS_DATE,DUTY_PLAN_ID,PAYROLL_ROLE,CAN_USE_BACKSTAGE,BACKSTAGE_PARTICIPANT,STARTED_AT,CONFIRMED_AT,CONFIRMED_BY)
            VALUES (@StaffId,@Date,@PlanId,@Role,@CanUseBackstage,@BackstageParticipant,@StartedAt,@Now,@ActorId);
            """, new { StaffId = staffId, Date = date.Date, PlanId = plan!.Id, Role = plan.PayrollRole,
                plan.CanUseBackstage, BackstageParticipant = plan.CanUseBackstage && plan.BackstageParticipant,
                StartedAt = startedAt, Now = now, ActorId = actorId }, tx, cancellationToken: ct));
        return await ReadAsync(connection, tx, staffId, date, ct);
    }

    public static async Task ConfirmAsync(MySqlConnection connection, MySqlTransaction tx, string staffId,
        DateTime date, string role, string reason, string actorId, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("確認或更正計薪職位必須填寫原因。", "PAYROLL_REASON_REQUIRED");
        if (role is not ("service" or "designated" or "manager" or "backstage")) throw Unresolved();
        var permissions = await connection.QuerySingleAsync<Plan>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM ADMIN_USERS WHERE STAFF_MEMBER_ID=@StaffId AND IS_ACTIVE=TRUE
                         AND ROLE_LEVEL='developer') AS CanUseBackstage,
                   EXISTS(SELECT 1 FROM ADMIN_USERS WHERE STAFF_MEMBER_ID=@StaffId AND IS_ACTIVE=TRUE
                         AND ROLE_LEVEL IN ('developer','manager')) AS CanUseManager;
            """, new { StaffId = staffId }, tx, cancellationToken: ct));
        if (role == "backstage" && !permissions.CanUseBackstage || role == "manager" && !permissions.CanUseManager)
            throw new BusinessException("此人員不具備該計薪職位資格。", "PAYROLL_ROLE_NOT_ALLOWED");
        // Match creation/finalization lock order before locking all runs for this day.
        await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT ID FROM ORDERING_RUNTIME_LOCKS WHERE ID='operating_period' FOR UPDATE;",
            transaction: tx, cancellationToken: ct));
        var finalized = await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT STATUS FROM SETTLEMENT_RUNS WHERE BUSINESS_DATE=@Date ORDER BY ID FOR UPDATE;
            """, new { Date = date.Date }, tx, cancellationToken: ct));
        var current = await ReadAsync(connection, tx, staffId, date, ct);
        if (current is null)
            throw new BusinessException("尚無上班職位紀錄；請先確認班表並開始上班。", "PAYROLL_NOT_STARTED");
        if (finalized.Any(status => status == "finalized") && current.PayrollRole is not null)
            throw new ConflictException("本營業日已有正式結算；請使用結算更正流程，不能改寫上班職位。", "SETTLEMENT_LOCKED");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE STAFF_PAYROLL_SNAPSHOTS SET PAYROLL_ROLE=@Role, CAN_USE_BACKSTAGE=@CanUseBackstage,
              VERSION=VERSION+1,CONFIRMED_AT=@Now,CONFIRMED_BY=@ActorId
            WHERE STAFF_MEMBER_ID=@StaffId AND BUSINESS_DATE=@Date;
            INSERT INTO STAFF_PAYROLL_SNAPSHOT_AUDIT
              (ID,STAFF_MEMBER_ID,BUSINESS_DATE,BEFORE_ROLE,AFTER_ROLE,SNAPSHOT_VERSION,REASON,CREATED_AT,CREATED_BY)
            VALUES (@Id,@StaffId,@Date,@BeforeRole,@Role,@Version,@Reason,@Now,@ActorId);
            UPDATE SETTLEMENT_RESULT_LINES L JOIN SETTLEMENT_RUNS R ON R.ID=L.SETTLEMENT_ID
            SET L.ANOMALY_STATUS='stale', L.ANOMALY_NOTE='上班計薪職位已更正，請核對輸入並重新計算。'
            WHERE R.BUSINESS_DATE=@Date AND R.STATUS<>'finalized';
            UPDATE SETTLEMENT_RUNS SET INPUT_VERSION=INPUT_VERSION+1,STATUS='draft',UPDATED_AT=@Now,UPDATED_BY=@ActorId
            WHERE BUSINESS_DATE=@Date AND STATUS<>'finalized';
            """, new { StaffId = staffId, Date = date.Date, Role = role, permissions.CanUseBackstage,
                Now = now, ActorId = actorId, Id = Guid.NewGuid().ToString("D"), BeforeRole = current.PayrollRole,
                Version = current.Version + 1, Reason = reason.Trim() }, tx, cancellationToken: ct));
    }

    private static BusinessException Unresolved()
        => new("請先由經理確認本班計薪職位；既有出勤須在結算出勤區確認上班時職位。", "PAYROLL_ROLE_UNRESOLVED");

    public static async Task CaptureStartedPlanAsync(MySqlConnection connection, MySqlTransaction tx,
        string staffId, DateTime date, DateTime now, string actorId, CancellationToken ct)
    {
        var start = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition("""
            SELECT TIMESTAMP(BUSINESS_DATE,START_TIME) FROM STAFF_DUTY_PLANS
            WHERE STAFF_MEMBER_ID=@StaffId AND BUSINESS_DATE=@Date AND IS_WORKING=TRUE
              AND APPROVAL_STATUS='approved' AND START_TIME IS NOT NULL FOR UPDATE;
            """, new { StaffId = staffId, Date = date.Date }, tx, cancellationToken: ct));
        if (start is not null && start <= now)
            await CaptureAsync(connection, tx, staffId, date, start.Value, actorId, now, false, ct);
    }

    private sealed class Plan
    {
        public string Id { get; set; } = "";
        public string? PayrollRole { get; set; }
        public bool CanUseBackstage { get; set; }
        public bool CanUseManager { get; set; }
        public bool BackstageParticipant { get; set; }
    }
}
