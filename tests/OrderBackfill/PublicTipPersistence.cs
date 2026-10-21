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
        foreach (var file in new[] { "settlement-base.sql", "settlement-sources.sql", "settlement-source-items.sql" })
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
        Check(saved.CalculationVersion == 4 && saved.PublicTipAmount == 101, "actual persistence saves current formula version and derived pool");
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
        Console.WriteLine("Public-tip database persistence checks passed (formula version, totals, hours, history and stale save rejection).");
    }
    private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); }
}
