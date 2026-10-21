using System.Reflection;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Services.Menu;
using ToBeClarify.Api.Services.Ordering;
using ToBeClarify.Api.Services.Admin.Settlement;

static class TipOrders
{
    public static void PolicyChecks()
    {
        OrderingFinanceTipStaffOptionDto[] staff = [new("a", "A", ["service"]), new("b", "B", ["designated"])];
        var equal = OrderTipDistributionPolicy.Calculate(101, "equal", [new("b", null), new("a", null)], staff);
        Check(equal.Allocations.Sum(x => x.Amount) == 101 && equal.Allocations[0].Amount == 51, "equal allocation preserves integer total");
        var custom = OrderTipDistributionPolicy.Calculate(101, "custom", [new("a", 30), new("b", 70)], staff);
        Check(custom.Allocations.Sum(x => x.Amount) == 101 && custom.Allocations[0].Amount == 30, "custom allocation preserves integer total");
        RejectSync(() => OrderTipDistributionPolicy.Calculate(100, "equal", [new("a", null)], staff));
        RejectSync(() => OrderTipDistributionPolicy.Calculate(100, "custom", [new("a", 40), new("b", 40)], staff));
        RejectSync(() => OrderTipDistributionPolicy.Calculate(100, "designated", [new("outsider", null)], staff));
        var service = new SettlementService(null!, null!, null!, null!);
        var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var rule = new SettlementRuleRow { TimeRoundMinutes = 30, MoneyRoundUnit = 1 };
        foreach (var cash in new[] { 0, 50, 100 })
        {
            var source = new SettlementSourceData {
                Orders = [new() { OrderId = "order", TotalAmount = 100, Subtotal = 100, TipAmount = 100 }],
                Tips = [new() { OrderId = "order", StaffId = "a", StaffAmount = 100, TipAmount = 100, SourceType = "order_tip" }],
                StaffInputs = [new() { Id = "input", StaffId = "a", Role = "service", ActualMinutes = 60 }],
                PayrollAssignments = [new() { StaffId = "a", PayrollRole = "service", ActualMinutes = 60, PayrollSnapshotVersion = 1 }],
                Cash = new() { HasCashRecords = true, NetCash = cash },
                CashAllocations = [new() { OrderId = "order", Kind = "cash_receipt", Amount = cash, SourceKind = "order",
                    CashPeriodId = "period", AllocationStatus = "confirmed", HoldScope = "none" }]
            };
            var result = calculate.Invoke(service, [new SettlementRunRow { DayType = "normal" }, rule, source])!;
            var lines = (IReadOnlyList<SettlementResultLineRow>)result.GetType().GetProperty("Results")!.GetValue(result)!;
            Check(lines.Single().DesignatedTip == cash, "tips recognize only the paid fraction");
        }
        Console.WriteLine("Tip order allocation and cash-recognition checks passed.");
    }

    public static async Task Integration(MySqlConnection db, AppDbContext context, NewOrderAggregate template, string periodId, string runId)
    {
        await db.ExecuteAsync("""
            CREATE TABLE STAFF_MEMBERS(ID CHAR(36) PRIMARY KEY,DISPLAY_NAME VARCHAR(100));
            CREATE TABLE SETTLEMENT_STAFF_INPUTS(STAFF_ID CHAR(36),SETTLEMENT_ID VARCHAR(36),ROLE VARCHAR(20),ACTUAL_MINUTES INT);
            CREATE TABLE ADMIN_USERS(ID VARCHAR(36),STAFF_MEMBER_ID CHAR(36));
            CREATE TABLE ORDERING_FINANCE_TIP_ALLOCATIONS(RECORD_ID VARCHAR(36),STAFF_ID CHAR(36),STAFF_NAME_SNAPSHOT VARCHAR(100),PERCENTAGE DECIMAL(8,4),AMOUNT BIGINT);
            CREATE TABLE ORDERING_SESSION_ADMISSIONS(SESSION_ID VARCHAR(36),STATUS VARCHAR(20));
            CREATE TABLE ORDERING_FINANCE_OPERATIONS(OPERATION_ID VARCHAR(36) PRIMARY KEY,SESSION_ID VARCHAR(36),PAYLOAD_HASH VARCHAR(64),RESULT_JSON LONGTEXT,CREATED_AT DATETIME,CREATED_BY VARCHAR(36));
            CREATE TABLE ORDERING_FINANCE_CASES(ID VARCHAR(36),RECORD_ID VARCHAR(36),STATUS VARCHAR(20),RESOLVED_AT DATETIME,RESOLVED_BY VARCHAR(36),RESOLUTION_NOTE TEXT,UPDATED_AT DATETIME,UPDATED_BY VARCHAR(36));
            INSERT INTO STAFF_MEMBERS VALUES('11111111-1111-4111-8111-111111111111','A'),('22222222-2222-4222-8222-222222222222','B');
            INSERT INTO SETTLEMENT_STAFF_INPUTS VALUES('11111111-1111-4111-8111-111111111111',@RunId,'service',60),('22222222-2222-4222-8222-222222222222',@RunId,'designated',120);
            """, new { RunId = runId });
        var finance = new OrderingFinanceRepository(context);
        var before = await finance.GetAccountAsync(template.SessionId, default);
        var staff = await finance.GetTipStaffOptionsAsync(template.SessionId, default);
        var distribution = OrderTipDistributionPolicy.Calculate(101, "equal", staff.Select(x => new SaveOrderingFinanceTipAllocationRequest(x.StaffId, null)).ToArray(), staff);
        var itemId = Guid.NewGuid().ToString("D");
        var order = template with { Id = Guid.NewGuid().ToString("D"), OrderNumber = "fixture-distributed-tip", Subtotal = 101, TotalAmount = 101,
            Items = [new(itemId, "tip", null, null, "employee tip", 101, 1, null, null, 101, "tip_allocation", 0)],
            Tips = [new(Guid.NewGuid().ToString("D"), itemId, null, null, 101, 100, 0, 101, 0) { Distribution = distribution }],
            Backfill = template.Backfill! with { OperationId = Guid.NewGuid().ToString("D"), ExpectedFinanceVersion = before.Version, LinkedCashRecordId = null },
            BackfillPayloadHash = "tip-payload", BackfillDuplicateHash = "tip-duplicate", QuoteId = Guid.NewGuid().ToString("D") };
        order = order with { QuoteFingerprint = MenuQuoteService.Fingerprint(order) };
        await db.ExecuteAsync("INSERT INTO MENU_QUOTES(ID,SESSION_ID,REQUEST_JSON,SNAPSHOT_JSON,FINGERPRINT,EXPIRES_AT) VALUES(@QuoteId,@SessionId,'{}',@Snapshot,@Hash,@Expiry);",
            new { order.QuoteId, order.SessionId, Snapshot = order.MenuSnapshotJson, Hash = order.QuoteFingerprint, Expiry = order.SubmittedAt.AddMinutes(5) });
        var repository = new OrderingRepository(context);
        await Task.WhenAll(repository.CreateOrderAsync(order, default), repository.CreateOrderAsync(order, default));
        var after = await finance.GetAccountAsync(order.SessionId, default);
        Check(after.Receivable == before.Receivable + 101 && after.CashReceived == before.CashReceived, "new tip creates receivable once and no receipt");
        var tips = await finance.GetOrderTipDistributionsAsync(order.SessionId, default);
        Check(tips.Single(x => x.ItemId == itemId).Distribution!.Allocations.Sum(x => x.Amount) == 101, "distribution readback preserves total");
        var received = new SaveOrderingFinanceRecordRequest { OperationId = Guid.NewGuid().ToString("D"), ExpectedVersion = after.Version,
            Kind = "cash_receipt", Amount = 101, SourceKind = "order", OrderId = order.Id, SourcePeriodId = periodId, CashPeriodId = periodId,
            OccurredAt = order.Backfill.OccurredAt, Reason = "actual receipt" };
        await finance.SaveAsync(order.SessionId, null, received, [], "receipt", "clerk", default);
        var paid = await finance.GetAccountAsync(order.SessionId, default);
        Check(paid.Receivable == after.Receivable && paid.CashReceived == after.CashReceived + 101 && paid.Balance == before.Balance, "receipt reduces balance without changing receivable");
        await db.ExecuteAsync("UPDATE SETTLEMENT_RUNS SET STATUS='finalized',CALCULATION_VERSION=3 WHERE ID=@Id;", new { Id = runId });
        var request = new CorrectOrderTipDistributionRequest { OperationId = Guid.NewGuid().ToString("D"), ExpectedAccountVersion = paid.Version,
            ExpectedDistributionVersion = 1, Mode = "custom", Allocations = [new(staff[0].StaffId, 30), new(staff[1].StaffId, 70)], Reason = "correct distribution" };
        await Task.WhenAll(finance.CorrectOrderTipAsync(order.SessionId, itemId, request, "correction", "clerk", default),
            finance.CorrectOrderTipAsync(order.SessionId, itemId, request, "correction", "clerk", default));
        Check(await db.ExecuteScalarAsync<int>("SELECT VERSION FROM ORDER_TIP_DISTRIBUTIONS WHERE ORDER_ITEM_ID=@Id", new { Id = itemId }) == 2, "retry changes distribution once");
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ORDER_AUDIT_LOG WHERE ACTION_TYPE='order.tip_distribution.corrected'") == 1, "one correction audit");
        Check(await db.ExecuteScalarAsync<int>("SELECT CALCULATION_VERSION FROM SETTLEMENT_RUNS WHERE ID=@Id", new { Id = runId }) == 3, "finalized version retained");
        var corrected = await finance.GetAccountAsync(order.SessionId, default);
        Check(corrected.Receivable == paid.Receivable && corrected.CashReceived == paid.CashReceived, "distribution correction does not affect money");
        await RejectAsync(() => finance.CorrectOrderTipAsync(order.SessionId, itemId, request with { OperationId = Guid.NewGuid().ToString("D"), ExpectedAccountVersion = corrected.Version }, "stale", "clerk", default));
        // Exercise the exact settlement distribution loader against the migration, without requiring unrelated tables.
        var sourceTips = new List<SettlementTipRevenueRow>();
        var loader = typeof(SettlementRepository).GetMethod("AddDistributedOrderTipsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)loader.Invoke(null, [db, null, new DateOnly(2026, 10, 18), sourceTips, CancellationToken.None])!;
        Check(sourceTips.Count == 2 && sourceTips.Sum(x => x.StaffAmount) == 101 && sourceTips.All(x => x.SourceType == "order_tip"), "settlement uses distribution as cash-recognized order tips");
        var legacy = Guid.NewGuid().ToString("D");
        await db.ExecuteAsync("""
            INSERT INTO ORDERING_FINANCE_RECORDS(ID,SESSION_ID,VERSION,KIND,AMOUNT,SOURCE_KIND,ORDER_ID,SOURCE_PERIOD_ID,OCCURRED_AT,ALLOCATION_STATUS,HOLD_SCOPE,REASON,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY)
            VALUES(@Id,@SessionId,1,'tip_supplement',101,'order',@OrderId,@PeriodId,@Now,'confirmed','none','legacy duplicate',@Now,'clerk',@Now,'clerk');
            """, new { Id = legacy, order.SessionId, OrderId = order.Id, PeriodId = periodId, Now = order.SubmittedAt });
        await RejectAsync(() => finance.CorrectOrderTipAsync(order.SessionId, itemId, request with { OperationId = Guid.NewGuid().ToString("D"), ExpectedAccountVersion = corrected.Version, ExpectedDistributionVersion = 2 }, "legacy", "clerk", default));
        var voidRequest = new VoidOrderingFinanceRecordRequest { OperationId = Guid.NewGuid().ToString("D"), ExpectedAccountVersion = corrected.Version, ExpectedRecordVersion = 1, Reason = "already in order" };
        await finance.VoidAsync(order.SessionId, legacy, voidRequest, "void-legacy", "clerk", default);
        var reconciled = await finance.GetAccountAsync(order.SessionId, default);
        Check(reconciled.Receivable == corrected.Receivable && reconciled.CashReceived == corrected.CashReceived, "void legacy allocation preserves money");
        Check(reconciled.Records.Single(x => x.Id == legacy).IsVoided, "legacy record retained as voided");
        Console.WriteLine("Tip order database integration checks passed.");
    }

    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    static void RejectSync(Action action) { try { action(); } catch (BusinessException) { return; } throw new Exception("Expected rejection"); }
    static async Task RejectAsync(Func<Task<OrderingFinanceOperationDto>> action) { try { await action(); } catch (Exception e) when (e is BusinessException or ConflictException) { return; } throw new Exception("Expected rejection"); }
}
