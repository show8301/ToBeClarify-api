using Dapper;
using MySqlConnector;
using Microsoft.Extensions.Configuration;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Admin.Attendance;
using ToBeClarify.Api.Repositories.Shared;

internal static class PayrollRoleIntegration
{
    public static async Task Run()
    {
        var setting = Environment.GetEnvironmentVariable("PAYROLL_ROLE_TEST_CONNECTION")
            ?? throw new Exception("Set a dedicated local payroll test connection.");
        var builder = new MySqlConnectionStringBuilder(setting);
        var database = builder.Database;
        if (builder.Server != "127.0.0.1" || builder.Port == 3306 || !database.StartsWith("codex_payroll_", StringComparison.Ordinal)
            || database.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new Exception("Only a dedicated loopback codex_payroll_* database on a non-production port is allowed.");
        builder.Database = "";
        await using (var admin = new MySqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4;");
        }
        await using var db = new MySqlConnection(setting);
        await db.OpenAsync();
        // Minimal synthetic schema. This suite never reads project connection settings or production rows.
        await db.ExecuteAsync("""
            CREATE TABLE ADMIN_USERS(STAFF_MEMBER_ID CHAR(36),IS_ACTIVE BOOL,ROLE_LEVEL VARCHAR(16)) ENGINE=InnoDB;
            CREATE TABLE STAFF_MEMBERS(ID CHAR(36) PRIMARY KEY,IS_ACTIVE BOOL,DISPLAY_NAME VARCHAR(100),SORT_ORDER INT) ENGINE=InnoDB;
            CREATE TABLE STAFF_ATTENDANCE_EVENTS(ID VARCHAR(36) PRIMARY KEY,STAFF_MEMBER_ID VARCHAR(36),BUSINESS_DATE DATE,
                DUTY_PLAN_ID VARCHAR(36),EVENT_TYPE VARCHAR(40),EVENT_AT DATETIME,EVENT_END_AT DATETIME,SOURCE VARCHAR(20),
                MINUTES_DELTA INT,BASE_EVENT_ID VARCHAR(36),OPERATION_ID VARCHAR(36) UNIQUE,REASON VARCHAR(500),CREATED_AT DATETIME,CREATED_BY VARCHAR(100)) ENGINE=InnoDB;
            CREATE TABLE STAFF_DUTY_PLANS(ID VARCHAR(36),STAFF_MEMBER_ID VARCHAR(36),BUSINESS_DATE DATE,
                START_TIME TIME,END_TIME TIME,IS_WORKING BOOL,APPROVAL_STATUS VARCHAR(20)) ENGINE=InnoDB;
            CREATE TABLE STAFF_DAILY_WORK_MODES(STAFF_MEMBER_ID VARCHAR(36),BUSINESS_DATE DATE,STOP_ACCEPTING_NEW_ORDERS BOOL) ENGINE=InnoDB;
            CREATE TABLE ORDER_NOMINEES(STAFF_ID VARCHAR(36),ORDER_ID VARCHAR(36),CONFIRMATION_STATUS VARCHAR(20),
                REQUESTED_STARTS_AT DATETIME,REQUESTED_SERVICE_ENDS_AT DATETIME) ENGINE=InnoDB;
            CREATE TABLE ORDERS(ID VARCHAR(36),SESSION_ID VARCHAR(36),ORDER_NUMBER VARCHAR(40),ORDER_STATUS VARCHAR(20)) ENGINE=InnoDB;
            CREATE TABLE CUSTOMER_ORDER_SESSIONS(ID VARCHAR(36),BUSINESS_DATE DATE,CUSTOMER_NAME VARCHAR(100)) ENGINE=InnoDB;
            CREATE TABLE ORDERING_RUNTIME_LOCKS(ID VARCHAR(50) PRIMARY KEY) ENGINE=InnoDB;
            CREATE TABLE SETTLEMENT_RUNS(ID CHAR(36) PRIMARY KEY,BUSINESS_DATE DATE,STATUS VARCHAR(20),
                INPUT_VERSION INT,UPDATED_AT DATETIME,UPDATED_BY VARCHAR(100),KEY(BUSINESS_DATE)) ENGINE=InnoDB;
            CREATE TABLE SETTLEMENT_RESULT_LINES(SETTLEMENT_ID CHAR(36),ANOMALY_STATUS VARCHAR(20),ANOMALY_NOTE VARCHAR(500),
                AFTER_ROUNDING BIGINT) ENGINE=InnoDB;
            CREATE TABLE STAFF_PAYROLL_SNAPSHOTS(STAFF_MEMBER_ID CHAR(36),BUSINESS_DATE DATE,DUTY_PLAN_ID CHAR(36),
                PAYROLL_ROLE VARCHAR(16),CAN_USE_BACKSTAGE BOOL,BACKSTAGE_PARTICIPANT BOOL,STARTED_AT DATETIME,
                VERSION INT,CONFIRMED_AT DATETIME,CONFIRMED_BY VARCHAR(100),PRIMARY KEY(STAFF_MEMBER_ID,BUSINESS_DATE)) ENGINE=InnoDB;
            CREATE TABLE STAFF_PAYROLL_SNAPSHOT_AUDIT(ID CHAR(36) PRIMARY KEY,STAFF_MEMBER_ID CHAR(36),BUSINESS_DATE DATE,
                BEFORE_ROLE VARCHAR(16),AFTER_ROLE VARCHAR(16),SNAPSHOT_VERSION INT,REASON VARCHAR(500),CREATED_AT DATETIME,CREATED_BY VARCHAR(100)) ENGINE=InnoDB;
            INSERT INTO ORDERING_RUNTIME_LOCKS VALUES('operating_period');
            INSERT INTO STAFF_MEMBERS VALUES('11111111-1111-4111-8111-111111111111',TRUE,'fixture staff',0);
            INSERT INTO SETTLEMENT_RUNS VALUES('finalized','2026-10-18','finalized',3,NULL,NULL),('draft','2026-10-18','calculated',5,NULL,NULL);
            INSERT INTO SETTLEMENT_RESULT_LINES VALUES('finalized','ok',NULL,900000),('draft','ok',NULL,900000);
            INSERT INTO STAFF_PAYROLL_SNAPSHOTS VALUES('11111111-1111-4111-8111-111111111111','2026-10-18',NULL,'service',FALSE,FALSE,'2026-10-18 21:00:00',1,NULL,NULL);
            """);
        var date = new DateTime(2026, 10, 18);
        var now = new DateTime(2026, 10, 20);
        async Task<bool> Correct(string role, int? version, bool rollback = false)
        {
            await using var connection = new MySqlConnection(setting);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            try
            {
                await PayrollSnapshotStore.ConfirmAsync(connection, tx, "11111111-1111-4111-8111-111111111111", date, role, "correct reviewed role", "manager", now, default, version);
                if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync();
                return true;
            }
            catch (ConflictException) { await tx.RollbackAsync(); return false; }
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Check(await Correct("designated", 1), "Finalized dates allow an audited role correction.");
        Check(await db.ExecuteScalarAsync<int>("SELECT VERSION FROM STAFF_PAYROLL_SNAPSHOTS") == 2, "Snapshot version increments.");
        Check(await db.ExecuteScalarAsync<string>("SELECT CONCAT(BEFORE_ROLE,':',AFTER_ROLE,':',REASON,':',CREATED_BY) FROM STAFF_PAYROLL_SNAPSHOT_AUDIT")
            == "service:designated:correct reviewed role:manager", "Before/after role, reason and actor are audited.");
        Check(await db.ExecuteScalarAsync<string>("SELECT STATUS FROM SETTLEMENT_RUNS WHERE ID='finalized'") == "finalized"
            && await db.ExecuteScalarAsync<int>("SELECT INPUT_VERSION FROM SETTLEMENT_RUNS WHERE ID='finalized'") == 3
            && await db.ExecuteScalarAsync<long>("SELECT AFTER_ROUNDING FROM SETTLEMENT_RESULT_LINES WHERE SETTLEMENT_ID='finalized'") == 900000
            && await db.ExecuteScalarAsync<string>("SELECT ANOMALY_STATUS FROM SETTLEMENT_RESULT_LINES WHERE SETTLEMENT_ID='finalized'") == "ok",
            "Finalized money, input version and result state remain intact until recalculation.");
        Check(await db.ExecuteScalarAsync<string>("SELECT STATUS FROM SETTLEMENT_RUNS WHERE ID='draft'") == "draft"
            && await db.ExecuteScalarAsync<int>("SELECT INPUT_VERSION FROM SETTLEMENT_RUNS WHERE ID='draft'") == 6,
            "Unfinalized calculations are invalidated for the entire day.");
        Check(!await Correct("service", 1), "Stale expected snapshot versions cannot overwrite a newer correction.");
        Check(!await Correct("service", null), "An existing finalized role cannot be overwritten without an expected snapshot version.");
        Check(await Correct("service", 2, rollback: true), "Rollback scenario executes.");
        Check(await db.ExecuteScalarAsync<int>("SELECT VERSION FROM STAFF_PAYROLL_SNAPSHOTS") == 2
            && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STAFF_PAYROLL_SNAPSHOT_AUDIT") == 1, "Role and audit roll back together.");
        foreach (var forbiddenRole in new[] { "manager", "backstage" })
        {
            try { await Correct(forbiddenRole, 2); throw new Exception("Ineligible payroll role accepted."); }
            catch (BusinessException) { }
        }
        await db.ExecuteAsync("INSERT INTO ADMIN_USERS VALUES('11111111-1111-4111-8111-111111111111',TRUE,'manager');");
        var races = await Task.WhenAll(Correct("service", 2), Correct("manager", 2));
        Check(races.Count(x => x) == 1, "Only one concurrent correction at the same expected version succeeds.");
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STAFF_PAYROLL_SNAPSHOT_AUDIT") == 2,
            "Rejected concurrent/stale/rollback requests cannot add duplicate audit entries.");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = setting }).Build();
        var repository = new AttendanceRepository(new AppDbContext(config));
        var request = new StaffAttendanceActionRequest { OperationId = Guid.NewGuid().ToString("D"),
            BusinessDate = "2026-10-18", StaffId = "11111111-1111-4111-8111-111111111111", Action = "confirm_payroll_role", PayrollRole = "designated",
            ExpectedPayrollSnapshotVersion = 3, Reason = "correct through attendance API" };
        var submitted = await repository.ApplyAsync(request.StaffId, request, new DateTimeOffset(now, TimeSpan.FromHours(8)), "manager", default);
        var retried = await repository.ApplyAsync(request.StaffId, request, new DateTimeOffset(now, TimeSpan.FromHours(8)), "manager", default);
        Check(submitted.Id == retried.Id
            && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STAFF_ATTENDANCE_EVENTS") == 1
            && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STAFF_PAYROLL_SNAPSHOT_AUDIT") == 3,
            "Actual attendance repository retries create only one operation and one role audit.");
        var overview = await repository.GetOverviewAsync(DateOnly.FromDateTime(date), request.StaffId,
            new DateTimeOffset(now, TimeSpan.FromHours(8)), default);
        Check(overview.Staff.Single().StaffId == request.StaffId && overview.Staff.Single().PayrollSnapshotVersion == 4
            && overview.Staff.Single().PayrollRole == "designated", "The correction dialog can read the latest role through actual CHAR(36) staff identifiers.");
        Console.WriteLine("Payroll role database checks passed: finalized preservation, invalidation, audit, permissions, rollback and concurrency.");
    }
}
