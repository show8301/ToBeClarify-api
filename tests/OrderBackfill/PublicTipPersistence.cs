using System.Reflection;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

static class PublicTipPersistence
{
    public static async Task Run(MySqlConnection db, AppDbContext context, string runId, string orderId)
    {
        // This fixture already enforces a dedicated loopback database before creating any schema.
        foreach (var file in new[] { "settlement-base.sql", "settlement-sources.sql", "settlement-source-items.sql", "settlement-meal-credit.sql", "settlement-meal-credit.sql" })
            await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file)));
        await db.ExecuteAsync("UPDATE SETTLEMENT_RUNS SET STATUS='draft' WHERE ID=@Id", new { Id = runId });
        var repository = new SettlementRepository(context);
        var stored = (await repository.GetRunAsync(new(2026, 10, 18), 1, default))!;
        var source = new SettlementSourceData {
            Orders = [new() { OrderId = orderId, OrderNumber = "fixture-public-tip", TotalAmount = 101, TipAmount = 101 }],
            Tips = [new() { OrderId = orderId, TipAmount = 101, SourceType = "public_tip" }],
            StaffInputs = [new() { StaffId = "11111111-1111-4111-8111-111111111111", Role = "service", ActualMinutes = 180 },
                new() { StaffId = "22222222-2222-4222-8222-222222222222", Role = "designated", ActualMinutes = 60 }],
            PayrollAssignments = [new() { StaffId = "11111111-1111-4111-8111-111111111111", PayrollRole = "service" },
                new() { StaffId = "22222222-2222-4222-8222-222222222222", PayrollRole = "designated" }],
            Cash = new() { HasCashRecords = true },
            CashAllocations = [new() { Id = "paid", OrderId = orderId, Kind = "cash_receipt", Amount = 101,
                SourceKind = "order", AllocationStatus = "confirmed", HoldScope = "none", CashPeriodId = "period" }]
        };
        var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var calculated = calculate.Invoke(new SettlementService(null!, null!, null!, null!),
            [stored, new SettlementRuleRow { TimeRoundMinutes = 30 }, source])!;
        var type = calculated.GetType();
        var data = new SaveCalculationData { Run = (SettlementRunRow)type.GetProperty("Run")!.GetValue(calculated)!,
            Results = (IReadOnlyList<SettlementResultLineRow>)type.GetProperty("Results")!.GetValue(calculated)! };
        // Inputs are tested by the payroll suite. This transaction isolates result/version/source persistence.
        await repository.SaveCalculationAsync(runId, data, "fixture-manager", DateTime.Now, default);
        var saved = (await repository.GetRunAsync(new(2026, 10, 18), 1, default))!;
        Check(saved.CalculationVersion == 5 && saved.PublicTipAmount == 101, "actual persistence saves current formula version and derived pool");
        Check(await db.ExecuteScalarAsync<int>("SELECT SUM(PUBLIC_TIP) FROM SETTLEMENT_RESULT_LINES WHERE SETTLEMENT_ID=@Id", new { Id = runId }) == 101,
            "saved personal tips conserve the pool");
        var snapshot = await db.QueryAsync<SettlementResultSourceRow>("""
            SELECT S.SOURCE_KIND AS Kind,S.SOURCE_LABEL AS Label,S.AMOUNT AS Amount,S.ORDER_ID AS OrderId
            FROM SETTLEMENT_RESULT_SOURCES S JOIN SETTLEMENT_RESULT_LINES R ON R.ID=S.RESULT_LINE_ID
              AND R.SOURCE_SNAPSHOT_ID=S.SOURCE_SNAPSHOT_ID WHERE R.SETTLEMENT_ID=@Id;
            """, new { Id = runId });
        Check(snapshot.Sum(x => x.Amount) == 101 && snapshot.All(x => x.Kind == "public_tip"
            && x.OrderId == orderId && x.Label.Contains("合計 4")), "readback retains order, work hours and exact source allocations");
        try { await repository.SaveCalculationAsync(runId, data, "fixture-manager", DateTime.Now, default); throw new Exception("stale save accepted"); }
        catch (ConflictException) { }
        data.Run.InputVersion = saved.InputVersion;
        await repository.SaveCalculationAsync(runId, data, "fixture-manager", DateTime.Now, default);
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SETTLEMENT_RESULT_SOURCES") == 4,
            "a newer calculation retains old source snapshots");
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SETTLEMENT_RESULT_SOURCES S JOIN SETTLEMENT_RESULT_LINES R ON R.ID=S.RESULT_LINE_ID AND R.SOURCE_SNAPSHOT_ID=S.SOURCE_SNAPSHOT_ID") == 2,
            "current results point only to their own source version");
        var latest = (await repository.GetRunAsync(new(2026, 10, 18), 1, default))!;
        var mealSource = new SettlementSourceData {
            Orders = [new() { OrderId = orderId, OrderNumber = "fixture-meal-credit", Subtotal = 160000,
                TotalAmount = 150000, MealCreditApplied = 10000 }],
            Items = [new() { ItemId = "fixture-nomination", OrderId = orderId, ItemType = "nomination_base",
                ReferenceId = "11111111-1111-4111-8111-111111111111", OriginalLineTotal = 150000, LineTotal = 150000 },
                new() { ItemId = "fixture-meal", OrderId = orderId, ItemType = "menu_item",
                    OriginalLineTotal = 10000, LineTotal = 10000, MealCreditAmount = 10000 }],
            StaffInputs = [new() { StaffId = "11111111-1111-4111-8111-111111111111", Role = "designated", ActualMinutes = 60 }],
            PayrollAssignments = [new() { StaffId = "11111111-1111-4111-8111-111111111111", PayrollRole = "designated" }],
            Cash = new() { HasCashRecords = true },
            CashAllocations = [new() { Id = "meal-paid", OrderId = orderId, Kind = "cash_receipt", Amount = 150000,
                SourceKind = "order", AllocationStatus = "confirmed", HoldScope = "none", CashPeriodId = "period" }]
        };
        var mealCalculated = calculate.Invoke(new SettlementService(null!, null!, null!, null!),
            [latest, new SettlementRuleRow { TimeRoundMinutes = 30, DesignatedSharePercentage = 70 }, mealSource])!;
        await repository.SaveCalculationAsync(runId, new SaveCalculationData {
            Run = (SettlementRunRow)type.GetProperty("Run")!.GetValue(mealCalculated)!,
            Results = (IReadOnlyList<SettlementResultLineRow>)type.GetProperty("Results")!.GetValue(mealCalculated)!
        }, "fixture-manager", DateTime.Now, default);
        var mealSnapshot = await db.QuerySingleAsync<SettlementResultSourceRow>("""
            SELECT S.ORIGINAL_AMOUNT AS OriginalAmount,S.CANCELLED_AMOUNT AS CancelledAmount,
              S.MEAL_CREDIT_AMOUNT AS MealCreditAmount,S.AFTER_CREDIT_AMOUNT AS AfterCreditAmount,
              S.ORDER_REVENUE_AMOUNT AS OrderRevenueAmount,S.ADJUSTED_REVENUE_AMOUNT AS AdjustedRevenueAmount,
              S.ORDER_PAYABLE_AMOUNT AS OrderPayableAmount,S.ORDER_COLLECTED_AMOUNT AS OrderCollectedAmount,
              S.BASIS_AMOUNT AS BasisAmount,S.AMOUNT AS Amount
            FROM SETTLEMENT_RESULT_SOURCES S JOIN SETTLEMENT_RESULT_LINES R ON R.ID=S.RESULT_LINE_ID
              AND R.SOURCE_SNAPSHOT_ID=S.SOURCE_SNAPSHOT_ID
            WHERE R.SETTLEMENT_ID=@Id AND R.ROLE='designated' AND S.SOURCE_KIND='designated_revenue';
            """, new { Id = runId });
        Check(mealSnapshot.OriginalAmount == 150000 && mealSnapshot.CancelledAmount == 0
            && mealSnapshot.MealCreditAmount == 0 && mealSnapshot.AfterCreditAmount == 150000
            && mealSnapshot.OrderRevenueAmount == 150000 && mealSnapshot.AdjustedRevenueAmount == 150000
            && mealSnapshot.OrderPayableAmount == 150000 && mealSnapshot.OrderCollectedAmount == 150000
            && mealSnapshot.BasisAmount == 150000 && mealSnapshot.Amount == 105000,
            "new meal-credit source columns round-trip through actual repository persistence");
        Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SETTLEMENT_RESULT_SOURCES WHERE AFTER_CREDIT_AMOUNT IS NULL") == 4,
            "previous source snapshots retain their original empty credit fields");
        Console.WriteLine("Meal credit database persistence checks passed (migration rerun, formula 5, source steps and historical snapshots).");
        Console.WriteLine("Public-tip database persistence checks passed (formula version, totals, hours, history and stale save rejection).");
    }
    private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); }
}
