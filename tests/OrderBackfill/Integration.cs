using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Services.Menu;

static class Integration
{
    public static async Task Run()
    {
        var setting = Environment.GetEnvironmentVariable("ORDER_BACKFILL_TEST_CONNECTION") ?? throw new Exception("Missing local test connection");
        var builder = new MySqlConnectionStringBuilder(setting);
        if (builder.Server != "127.0.0.1" || builder.Port == 3306 || !builder.Database.StartsWith("codex_backfill_", StringComparison.Ordinal))
            throw new Exception("Integration tests require a dedicated loopback server on a non-production port and a codex_backfill_* database.");
        var database = builder.Database;
        if (database.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new Exception("Unsafe test database name");
        builder.Database = "";
        await using (var admin = new MySqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        await using var db = new MySqlConnection(setting);
        await db.OpenAsync();
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql")));
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migration.sql")));
        // The new migration is additive and rerunnable.
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migration.sql")));
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "tip-migration.sql")));
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "tip-migration.sql")));
        var now = new DateTime(2026, 10, 20, 20, 0, 0);
        var actual = new DateTime(2026, 10, 18, 22, 0, 0);
        var periodId = Guid.NewGuid().ToString("D");
        var sessionId = Guid.NewGuid().ToString("D");
        var cashId = Guid.NewGuid().ToString("D");
        var runId = Guid.NewGuid().ToString("D");
        await db.ExecuteAsync("""
            INSERT INTO ORDERING_RUNTIME_LOCKS (ID) VALUES ('operating_period');
            INSERT INTO BUSINESS_PERIODS (ID,BUSINESS_DATE,STARTS_AT,ENDS_AT,ACTUAL_OPENED_AT,ACTUAL_CLOSED_AT,
                PERIOD_STATUS,INTAKE_MODE,FLOW_VERSION,CREATED_AT,UPDATED_AT)
            VALUES (@PeriodId,'2026-10-18','2026-10-18 21:00:00','2026-10-19 05:00:00','2026-10-18 21:00:00',
                '2026-10-19 05:00:00','settled','staff_only',2,@Now,@Now);
            INSERT INTO CUSTOMER_ORDER_SESSIONS (ID,GAME_ID,CUSTOMER_NAME,BUSINESS_DATE,ACCESS_TOKEN_HASH,RECOVERY_CODE_HASH,
                PREPAID_MEAL_CREDIT,REMAINING_MEAL_CREDIT,SESSION_STATUS,BUSINESS_PERIOD_ID,CREATED_AT,UPDATED_AT)
            VALUES (@SessionId,'fixture','fixture','2026-10-18',REPEAT('a',64),REPEAT('b',64),10000,10000,'readonly',@PeriodId,@Now,@Now);
            INSERT INTO ORDERING_FINANCE_ACCOUNTS (SESSION_ID,SOURCE_PERIOD_ID,VERSION,UPDATED_AT) VALUES (@SessionId,@PeriodId,0,@Now);
            INSERT INTO ORDERING_FINANCE_RECORDS (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,CASH_PERIOD_ID,
                OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@CashId,@SessionId,1,'cash_receipt',100000,'unallocated',@PeriodId,@PeriodId,@Actual,'confirmed','none','already paid',@Now,'clerk',@Now,'clerk');
            INSERT INTO SETTLEMENT_RUNS (ID,BUSINESS_DATE,SESSION_NO,DAY_TYPE,STATUS,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY,FINALIZED_AT)
            VALUES (@RunId,'2026-10-18',1,'normal','finalized',@Now,'manager',@Now,'manager',@Now);
            """, new { PeriodId = periodId, SessionId = sessionId, CashId = cashId, RunId = runId, Now = now, Actual = actual });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = setting }).Build();
        var context = new AppDbContext(config);
        var repository = new OrderingRepository(context);
        var nomineeId = Guid.NewGuid().ToString("D");
        var nominationItemId = Guid.NewGuid().ToString("D");
        var roomItemId = Guid.NewGuid().ToString("D");
        var tipItemId = Guid.NewGuid().ToString("D");
        var orderId = Guid.NewGuid().ToString("D");
        var quoteId = Guid.NewGuid().ToString("D");
        var request = new OrderBackfillRequest { OperationId = Guid.NewGuid().ToString("D"), Reason = "forgotten completed sale",
            OccurredAt = new DateTimeOffset(actual, TimeSpan.FromHours(8)), LinkedCashRecordId = cashId,
            ExpectedFinanceVersion = 0, ExpectedCashRecordVersion = 1 };
        var order = new NewOrderAggregate(orderId, sessionId, "fixture-001", "standard", null, "completed", "staff_only", "not_required", null,
            now, 470000, 5000, 465000, null,
            [new(Guid.NewGuid().ToString("D"), "menu_item", "meal", null, "meal", 20000, 1, null, null, 20000, "fixed", 0),
             new(nominationItemId, "nomination_base", "staff", null, "nomination", 300000, 1, 1, 20, 300000, "per_segment", 1),
             new(roomItemId, "room_service", "room", null, "room", 50000, 1, 1, 20, 50000, "per_segment", 2),
             new(tipItemId, "tip", null, null, "tip", 100000, 1, null, null, 100000, "tip_allocation", 3)],
            [new(nomineeId, "staff", "staff name", null, "companionship", "companionship", 1, 20, 20, 20, 0, actual, actual.AddMinutes(20), actual.AddMinutes(20))],
            [new(Guid.NewGuid().ToString("D"), roomItemId, "room", "room", actual.Date, actual, actual.AddMinutes(20), 1, 20, 50000, 50000, "completed")],
            [new(Guid.NewGuid().ToString("D"), tipItemId, null, null, 100000, 0, 100, 0, 100000)]) { ActorType = "staff", ActorId = "clerk", ActorRole = "service", Backfill = request,
            BackfillPayloadHash = "payload", BackfillDuplicateHash = "duplicate", QuoteId = quoteId,
            MenuSnapshotJson = JsonSerializer.Serialize(new MenuOrderSnapshot([], [], 10000, 300000, 20, 10000), MenuPolicies.Json) };
        order = order with { QuoteFingerprint = MenuQuoteService.Fingerprint(order) };
        await db.ExecuteAsync("""
            INSERT INTO MENU_QUOTES (ID,SESSION_ID,REQUEST_JSON,SNAPSHOT_JSON,FINGERPRINT,EXPIRES_AT)
            VALUES (@QuoteId,@SessionId,'{}',@Snapshot,@Hash,@Expiry);
            """, new { QuoteId = quoteId, SessionId = sessionId, Snapshot = order.MenuSnapshotJson, Hash = order.QuoteFingerprint, Expiry = now.AddMinutes(5) });
        var ids = await Task.WhenAll(repository.CreateOrderAsync(order, default), repository.CreateOrderAsync(order, default));
        Assert(ids.All(id => id == orderId), "concurrent retries return one order");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDERS") == 1, "one order only");
        Assert(await db.ExecuteScalarAsync<int>("SELECT REMAINING_MEAL_CREDIT FROM CUSTOMER_ORDER_SESSIONS") == 5000, "credit deducted once");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDER_FULFILLMENT_UNITS WHERE UNIT_STATUS='completed' AND COMPLETED_QUANTITY=QUANTITY AND ACTUAL_ENDS_AT IS NOT NULL") == 4, "all units completed with actual times");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STAFF_BUSY_BLOCKS WHERE BLOCK_STATUS='active'") == 0, "no current busy reservation");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDERING_FINANCE_RECORDS WHERE KIND='cash_receipt'") == 1, "cash not duplicated");
        Assert(await db.ExecuteScalarAsync<long>("SELECT SUM(AMOUNT) FROM ORDERING_FINANCE_RECORDS WHERE KIND='cash_receipt'") == 100000, "cash unchanged");
        Assert(await db.ExecuteScalarAsync<string>("SELECT ORDER_ID FROM ORDERING_FINANCE_RECORDS") == orderId, "existing receipt linked");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDERING_FINANCE_REVISIONS") == 1, "receipt association audited");
        Assert(await db.ExecuteScalarAsync<string>("SELECT STATUS FROM SETTLEMENT_RUNS") == "finalized", "old settlement retained");
        Assert(await new SettlementRepository(context).GetPendingOrderBackfillCountAsync(runId, default) == 1, "pending recalculation notice");
        Assert((await repository.GetOrderAsync(orderId, default)).Orders.Single().BackfillJson is not null, "source metadata can be read back");
        var markApplied = typeof(SettlementRepository).GetMethod("MarkOrderBackfillsAppliedAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Missing settlement application hook");
        await using (var tx = await db.BeginTransactionAsync())
        {
            await (Task)markApplied.Invoke(null, [db, tx, runId, actual.Date, CancellationToken.None])!;
            await tx.RollbackAsync();
        }
        Assert(await new SettlementRepository(context).GetPendingOrderBackfillCountAsync(runId, default) == 1, "failed settlement keeps the pending backfill");
        await using (var tx = await db.BeginTransactionAsync())
        {
            await (Task)markApplied.Invoke(null, [db, tx, runId, actual.Date, CancellationToken.None])!;
            await tx.CommitAsync();
        }
        Assert(await new SettlementRepository(context).GetPendingOrderBackfillCountAsync(runId, default) == 0, "confirmed settlement clears exactly the applied backfill");
        await Reject(() => repository.CreateOrderAsync(order with { BackfillPayloadHash = "changed" }, default), "ORDER_BACKFILL_OPERATION_CONFLICT");
        await Reject(() => repository.CreateOrderAsync(order with { Backfill = request with { OperationId = Guid.NewGuid().ToString("D") } }, default), "ORDER_BACKFILL_DUPLICATE");
        // Fail after the quote/credit/items are written: the whole transaction must roll back.
        var failedId = Guid.NewGuid().ToString("D");
        var failedQuote = Guid.NewGuid().ToString("D");
        var failed = order with { Id = failedId, OrderNumber = "fixture-failed", QuoteId = failedQuote,
            Backfill = request with { OperationId = Guid.NewGuid().ToString("D"), LinkedCashRecordId = null },
            BackfillDuplicateHash = "new-content", MealCreditApplied = 0, Subtotal = 10000, TotalAmount = 10000,
            Items = [new(Guid.NewGuid().ToString("D"), "menu_item", "meal", null, "meal", 10000, 1, null, null, 10000, "fixed", 0)],
            Nominees = [], Rooms = [], Tips = [],
            MenuSnapshotJson = JsonSerializer.Serialize(new MenuOrderSnapshot([], [], 10000, 300000, 20, 5000), MenuPolicies.Json) };
        failed = failed with { QuoteFingerprint = MenuQuoteService.Fingerprint(failed) };
        await db.ExecuteAsync("INSERT INTO MENU_QUOTES (ID,SESSION_ID,REQUEST_JSON,SNAPSHOT_JSON,FINGERPRINT,EXPIRES_AT) VALUES (@Id,@SessionId,'{}',@Snapshot,@Hash,@Expiry)",
            new { Id = failedQuote, SessionId = sessionId, Snapshot = failed.MenuSnapshotJson, Hash = failed.QuoteFingerprint, Expiry = now.AddMinutes(5) });
        await Reject(() => repository.CreateOrderAsync(failed, default), "FINANCE_VERSION_CONFLICT");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDERS") == 1, "failed write rolled back");
        Assert(await db.ExecuteScalarAsync<string?>("SELECT ORDER_ID FROM MENU_QUOTES WHERE ID=@Id", new { Id = failedQuote }) is null, "failed quote consumption rolled back");
        await Reject(() => repository.CreateOrderAsync(failed with { Backfill = null, ActorType = "customer" }, default), "BUSINESS_PERIOD_NOT_OPEN");
        await Reject(() => repository.CreateOrderAsync(failed with { Backfill = failed.Backfill! with {
            ExpectedFinanceVersion = 1, LinkedCashRecordId = cashId, ExpectedCashRecordVersion = 2 } }, default), "ORDER_BACKFILL_CASH_CHANGED");
        var otherCashId = Guid.NewGuid().ToString("D");
        var refundId = Guid.NewGuid().ToString("D");
        var existingTipId = Guid.NewGuid().ToString("D");
        await db.ExecuteAsync("""
            INSERT INTO ORDERING_FINANCE_RECORDS (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,CASH_PERIOD_ID,
                OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@CashId,@SessionId,1,'cash_receipt',10000,'unallocated',@PeriodId,@PeriodId,@Now,'confirmed','none','mixed cash',@Now,'clerk',@Now,'clerk');
            INSERT INTO ORDERING_FINANCE_RECORDS (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,CASH_PERIOD_ID,
                OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,REVERSES_RECORD_ID,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@RefundId,@SessionId,1,'cash_refund',100,'unallocated',@PeriodId,@PeriodId,@Now,'confirmed','none','refund',@CashId,@Now,'clerk',@Now,'clerk');
            """, new { CashId = otherCashId, RefundId = refundId, SessionId = sessionId, PeriodId = periodId, Now = now });
        await Reject(() => repository.CreateOrderAsync(failed with { Backfill = failed.Backfill! with {
            ExpectedFinanceVersion = 1, LinkedCashRecordId = otherCashId, ExpectedCashRecordVersion = 1 } }, default), "ORDER_BACKFILL_CASH_REFUNDED");
        await db.ExecuteAsync("""
            UPDATE ORDERING_FINANCE_RECORDS SET VOIDED_AT=@Now WHERE ID=@RefundId;
            INSERT INTO ORDERING_FINANCE_RECORDS (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,
                OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,LINKED_CASH_RECORD_ID,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@Id,@SessionId,1,'tip_supplement',10000,'unallocated',@PeriodId,@Now,'confirmed','none','already distributed',@CashId,@Now,'clerk',@Now,'clerk');
            """, new { Id = existingTipId, RefundId = refundId, CashId = otherCashId, SessionId = sessionId, PeriodId = periodId, Now = now });
        var tipItem = Guid.NewGuid().ToString("D");
        var duplicateTip = failed with { Id = Guid.NewGuid().ToString("D"), OrderNumber = "fixture-tip-duplicate",
            Items = [new(tipItem, "tip", null, null, "tip", 10000, 1, null, null, 10000, "tip_allocation", 0)],
            Tips = [new(Guid.NewGuid().ToString("D"), tipItem, null, null, 10000, 0, 100, 0, 10000)],
            BackfillDuplicateHash = "tip-content", Backfill = failed.Backfill! with { OperationId = Guid.NewGuid().ToString("D"),
                ExpectedFinanceVersion = 1, LinkedCashRecordId = otherCashId, ExpectedCashRecordVersion = 1 } };
        duplicateTip = await Quote(db, duplicateTip, now);
        await Reject(() => repository.CreateOrderAsync(duplicateTip, default), "TIP_LEGACY_RECONCILIATION_REQUIRED");
        await db.ExecuteAsync("UPDATE ORDERING_FINANCE_RECORDS SET VOIDED_AT=@Now WHERE ID IN @Ids;",
            new { Now = now, Ids = new[] { otherCashId, existingTipId } });
        var chargeId = Guid.NewGuid().ToString("D");
        await db.ExecuteAsync("""
            INSERT INTO ORDERING_FINANCE_RECORDS (ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,SOURCE_PERIOD_ID,
                OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES (@Id,@SessionId,1,'charge_add',10000,'unallocated',@PeriodId,@Now,'confirmed','none','previous charge',@Now,'clerk',@Now,'clerk');
            """, new { Id = chargeId, SessionId = sessionId, PeriodId = periodId, Now = now });
        await Reject(() => repository.CreateOrderAsync(failed with { Backfill = failed.Backfill! with {
            ExpectedFinanceVersion = 1 } }, default), "ORDER_BACKFILL_CHARGE_DUPLICATE");
        Assert(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDERS") == 1, "cash and duplicate-charge failures leave no extra order");
        await db.ExecuteAsync("""
            UPDATE ORDERING_FINANCE_RECORDS SET VOIDED_AT=@Now WHERE ID=@Id;
            UPDATE BUSINESS_PERIODS SET PERIOD_STATUS='closed';
            UPDATE SETTLEMENT_RUNS SET STATUS='calculated',INPUT_VERSION=3;
            """, new { Id = chargeId, Now = now });
        await repository.CreateOrderAsync(failed with { Backfill = failed.Backfill! with { ExpectedFinanceVersion = 1 } }, default);
        Assert(await db.ExecuteScalarAsync<string>("SELECT STATUS FROM SETTLEMENT_RUNS") == "draft", "closed-period backfill invalidates an unfinalized calculation");
        Assert(await db.ExecuteScalarAsync<int>("SELECT INPUT_VERSION FROM SETTLEMENT_RUNS") == 4, "old calculated input version cannot finalize");
        Assert(await db.ExecuteScalarAsync<long>("SELECT SUM(AMOUNT) FROM ORDERING_FINANCE_RECORDS WHERE KIND='cash_receipt' AND VOIDED_AT IS NULL") == 100000, "unlinked backfill never invents cash");
        await TipOrders.Integration(db, context, failed, periodId, runId);
        Console.WriteLine("Local MariaDB transaction checks passed (concurrency, cash totals, rollback, completed service, settlement retention).");
    }
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); }
    static async Task<NewOrderAggregate> Quote(MySqlConnection db, NewOrderAggregate order, DateTime now)
    {
        order = order with { QuoteId = Guid.NewGuid().ToString("D"), QuoteFingerprint = MenuQuoteService.Fingerprint(order) };
        await db.ExecuteAsync("INSERT INTO MENU_QUOTES (ID,SESSION_ID,REQUEST_JSON,SNAPSHOT_JSON,FINGERPRINT,EXPIRES_AT) VALUES (@Id,@SessionId,'{}',@Snapshot,@Hash,@Expiry)",
            new { Id = order.QuoteId, order.SessionId, Snapshot = order.MenuSnapshotJson, Hash = order.QuoteFingerprint, Expiry = now.AddMinutes(5) });
        return order;
    }
    static async Task Reject(Func<Task<string>> action, string expectedCode)
    {
        try { await action(); }
        catch (Exception ex) when (ex is BusinessException or ConflictException)
        {
            var code = ex is BusinessException business ? business.ErrorCode : ((ConflictException)ex).ErrorCode;
            Assert(code == expectedCode, $"Expected {expectedCode}, got {code}");
            return;
        }
        throw new Exception($"Expected rejection {expectedCode}");
    }
}
