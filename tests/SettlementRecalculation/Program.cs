using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ToBeClarify.Api.Controllers.Admin;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

var checks = 0;
var service = new SettlementService(null!, null!, null!, null!);
var prepare = typeof(SettlementService).GetMethod("PrepareRecalculation", BindingFlags.NonPublic | BindingFlags.Instance)!;
var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Instance)!;
var token = typeof(SettlementService).GetMethod("RecalculationToken", BindingFlags.NonPublic | BindingFlags.Static)!;
var rule = new SettlementRuleRow
{
    Id = "original-rule", DayType = "normal", TimeRoundMinutes = 30, ServiceManagerHourlyRate = 50000,
    ServiceManagerPoolPercentage = 25, BackstagePoolPercentage = 25, CompanyPercentage = 50,
    DesignatedSharePercentage = 70, DesignatedHourlyRate = 30000
};
var run = new SettlementRunRow
{
    Id = "settlement", BusinessDate = new(2026, 10, 18), SessionNo = 1, Status = "finalized",
    RuleVersionId = rule.Id, RuleSnapshotJson = JsonSerializer.Serialize(rule), InputVersion = 3,
    FinalizedAt = new(2026, 10, 19, 2, 0, 0), FinalizedBy = "manager", CalculationVersion = 2
};
var source = new SettlementSourceData
{
    Period = new() { PeriodStatus = "settled" },
    StaffInputs = [new() { Id = "input", StaffId = "xiaomi", DisplayName = "小米", Role = "manager",
        ActualMinutes = 57, AttendanceSource = "clock", PayrollSnapshotVersion = 1 }],
    PayrollAssignments = [new() { StaffId = "xiaomi", DisplayName = "小米", PayrollRole = "manager",
        PayrollSnapshotVersion = 1, ActualMinutes = 117 }],
    AttendanceEvents = [new() { StaffId = "xiaomi", Source = "manual", EventType = "manual_interval",
        EventAt = new(2026, 10, 18, 23, 2, 30), EventEndAt = new(2026, 10, 19, 1, 0, 0) }]
};
var original = calculate.Invoke(service, [run, rule, source])!;
var originalResults = (IReadOnlyList<SettlementResultLineRow>)original.GetType().GetProperty("Results")!.GetValue(original)!;
source = source with { Results = originalResults };
var payments = new SettlementPaymentEventRow[]
{
    new() { Id = "paid", StaffId = "xiaomi", EventKind = "payout", Amount = 100000 },
    new() { Id = "recovered", StaffId = "xiaomi", EventKind = "recovery", Amount = 10000 }
};
var corrections = new SettlementCorrectionEventRow[]
{
    new() { Id = "manual", StaffId = "xiaomi", AmountDelta = 2000, Status = "open" },
    new() { Id = "cancelled", StaffId = "xiaomi", AmountDelta = 99999, Status = "cancelled" }
};
var plan = Prepare(source);
Check(plan.Calculation.StaffInputs.Single().ActualMinutes == 117, "Corrected attendance replaces the stale 57-minute payroll input.");
Check(plan.Calculation.Results.Single().PayableHours == 2, "117 minutes uses the original 30-minute cutoff and pays two hours.");
Check(plan.Preview.Changes.Single().PreviousMinutes == 57 && plan.Preview.Changes.Single().CorrectedMinutes == 117,
    "Preview exposes both old and corrected minutes.");
Check(plan.Preview.PreviousPayroll == 50000 && plan.Preview.CorrectedPayroll == 100000 && plan.Preview.PayrollDelta == 50000,
    "The original hourly rule determines the corrected entitlement and delta.");
Check(source.StaffInputs.Single().ActualMinutes == 57 && source.Results.Single().PayableHours == 1,
    "Preview does not mutate the stored input or original result snapshot.");
Check(plan.Calculation.Run.FinalizedAt == run.FinalizedAt && plan.Calculation.Run.FinalizedBy == run.FinalizedBy,
    "The original finalization identity and timestamp survive correction.");
Check(plan.Preview.Balances.Single().NetPaid == 90000 && plan.Preview.Balances.Single().ManualCorrection == 2000
    && plan.Preview.Balances.Single().Outstanding == 12000, "Balances preserve payout/recovery and active manual corrections.");
Check(payments[0].Amount == 100000 && corrections[0].AmountDelta == 2000, "Preparing a revision never modifies payment or correction events.");
Check(plan.Preview.HasChanges && plan.Preview.Anomalies.Count == 0, "A complete settled period can preview a correction without spurious anomalies.");
Check(Prepare(source with { Period = new() { PeriodStatus = "open" } }).Preview.Anomalies
    .Any(x => x.Code == "RECALCULATION_PERIOD_NOT_READY"), "An open period blocks confirmation.");
Check(Prepare(source with { AttendanceBackfillRequests = [new() { Status = "pending" }] }).Preview.Anomalies
    .Any(x => x.Code == "RECALCULATION_BACKFILL_PENDING"), "Pending attendance approval blocks confirmation.");
Check(Prepare(source with { AttendanceEvents = [] }).Calculation.StaffInputs.Single().ActualMinutes == 57,
    "Missing attendance events never erase existing payroll input.");
var settled = source with { StaffInputs = plan.Calculation.StaffInputs, Results = plan.Calculation.Results };
Check(!Prepare(settled).Preview.HasChanges, "Repeated recalculation of the same corrected source produces no new revision.");
var reordered = token.Invoke(null, [new { rows = new[] { new { id = "a", amount = 1 }, new { id = "b", amount = 2 } } }]);
var same = token.Invoke(null, [new { rows = new[] { new { id = "b", amount = 2 }, new { id = "a", amount = 1 } } }]);
Check(Equals(reordered, same), "SQL row order does not invalidate a preview.");
var altered = token.Invoke(null, [new { rows = new[] { new { id = "a", amount = 3 }, new { id = "b", amount = 2 } } }]);
Check(!Equals(reordered, altered), "A source amount change invalidates the previous preview token.");
var paymentChange = payments.Append(new() { Id = "new-payment", StaffId = "xiaomi", EventKind = "payout", Amount = 1 }).ToArray();
var changedPaymentPlan = (SettlementRecalculationPlan)prepare.Invoke(service, [run, source, paymentChange, corrections])!;
Check(changedPaymentPlan.Preview.SourceToken != plan.Preview.SourceToken, "A payment recorded after preview requires rechecking balances.");
var confirmation = new ConfirmSettlementRecalculationRequest
{
    SourceToken = plan.Preview.SourceToken, ExpectedCorrectionVersion = plan.Preview.CorrectionVersion,
    OperationId = Guid.NewGuid().ToString("D"), Reason = "核對小米的出勤補正"
};
plan.ValidateConfirmation(confirmation);
checks++;
Reject(() => plan.ValidateConfirmation(new() { SourceToken = new string('0', 64),
    ExpectedCorrectionVersion = plan.Preview.CorrectionVersion }), "A stale source token cannot be confirmed.");
Reject(() => plan.ValidateConfirmation(new() { SourceToken = plan.Preview.SourceToken,
    ExpectedCorrectionVersion = plan.Preview.CorrectionVersion + 1 }), "A stale revision cannot be confirmed.");
Reject(() => (plan with { Preview = plan.Preview with { HasChanges = false } }).ValidateConfirmation(confirmation),
    "Unchanged payroll cannot create a duplicate revision.");
Reject(() => (plan with { Preview = plan.Preview with { Anomalies = [new("BLOCKED", "異常")] } }).ValidateConfirmation(confirmation),
    "A preview with blocking anomalies cannot be confirmed.");
var auditType = typeof(SettlementRepository).GetNestedType("RecalculationAuditPayload", BindingFlags.NonPublic)!;
var audit = Activator.CreateInstance(auditType, [confirmation.OperationId, plan.Preview.SourceToken, 1,
    confirmation.Reason, "manager", plan.Preview, plan.Calculation.Run,
    plan.Calculation.StaffInputs, plan.Calculation.Results])!;
Check(JsonSerializer.Deserialize(JsonSerializer.Serialize(audit, auditType), auditType) is not null,
    "Archived results can be restored for an idempotent retry.");
run.AdmissionFeeSnapshot = 10000;
var legacy = Prepare(source with { Admissions = [new() { SessionCount = 1, AdmissionFeeText = "99999" }] });
Check(legacy.Calculation.Run.AdmissionRevenue == 10000 && legacy.Calculation.Run.AdmissionFeeOverride is null,
    "A later admission price does not alter the finalized legacy fee snapshot.");
run.AdmissionFeeSnapshot = 0;
foreach (var method in new[] { "PreviewRecalculation", "ConfirmRecalculation", "RecalculationHistory" })
    Check(typeof(SettlementController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>()
        .Any(x => x.Policy == "AdminManager"), method + " requires manager authorization.");
var originalJson = run.RuleSnapshotJson;
run.RuleSnapshotJson = "{broken}";
try { Prepare(source); throw new Exception("Invalid snapshot was accepted."); }
catch (TargetInvocationException ex) when (ex.InnerException is BusinessException) { checks++; }
run.RuleSnapshotJson = originalJson;
Console.WriteLine($"Passed {checks} settlement recalculation memory checks; no database connections were used.");

SettlementRecalculationPlan Prepare(SettlementSourceData value)
    => (SettlementRecalculationPlan)prepare.Invoke(service, [run, value, payments, corrections])!;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
void Reject(Action action, string description)
{
    try { action(); }
    catch (Exception ex) when (ex is BusinessException or ConflictException) { checks++; return; }
    throw new Exception(description);
}
