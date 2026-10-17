using System.Reflection;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

var start = new DateTime(2026, 10, 16, 0, 36, 0);
var events = new AttendanceMinuteEvent[]
{
    new() { EventType = "scheduled_end", EventAt = start.AddHours(-1), MinutesDelta = 120 },
    new() { EventType = "manual_interval", EventAt = start, EventEndAt = start.AddHours(1), MinutesDelta = 60 },
    new() { EventType = "manual_interval", EventAt = start.AddMinutes(30), EventEndAt = start.AddHours(1), MinutesDelta = 30 }
};
Check(AttendanceMinutes.Effective(events) == 60, "Manual attendance replaces scheduled time; overlapping corrections do not double payroll.");
Check(AttendanceMinutes.Effective(events.Append(new() { EventType = "adjustment", MinutesDelta = -90 })) == 0,
    "Deductions cannot create negative payable time.");
var clockEvents = new AttendanceMinuteEvent[]
{
    new() { Id = "in", EventType = "clock_in", EventAt = start },
    new() { EventType = "clock_out", BaseEventId = "in", EventAt = start.AddMinutes(45), MinutesDelta = 45 },
    new() { EventType = "scheduled_end", EventAt = start.AddHours(2), MinutesDelta = 120 }
};
Check(AttendanceMinutes.Effective(clockEvents) == 45, "A completed real shift takes precedence over the scheduled fallback.");

var candidate = new SettlementAttendanceCandidateRow
{
    PayrollRole = "designated", PayrollSnapshotVersion = 1, StaffId = "staff", DisplayName = "三月五日", ScheduledRolesJson = "[\"designated\"]",
    ActualMinutes = AttendanceMinutes.Effective(events), IsActive = true
};
var inputs = new List<SettlementStaffInputRow>();
Check(SettlementAttendanceSync.MergeMissingInputs("run", inputs, [candidate]).Count == 0, "An approved single role imports without anomalies.");
Check(inputs.Single().ActualMinutes == 60 && inputs.Single().Role == "designated", "The real 60-minute attendance reaches the designated payroll input.");
SettlementAttendanceSync.MergeMissingInputs("run", inputs, [candidate]);
Check(inputs.Count == 1, "Repeated rechecks do not duplicate payroll inputs.");
inputs[0].IsBackstageParticipant = false;
inputs[0].ActivityHours = 2;
inputs[0].Note = "manager allocation";
candidate.ScheduledRolesJson = "[\"backstage\"]";
candidate.CanUseBackstage = true; candidate.BackstageParticipant = true;
SettlementAttendanceSync.MergeMissingInputs("run", inputs, [candidate]);
Check(inputs.Count == 2 && inputs[0].Role == "designated" && inputs[0].ActivityHours == 2 && inputs[0].Note == "manager allocation",
    "Backstage participation can be added without overwriting an explicit front-of-house allocation.");
var ambiguous = new List<SettlementStaffInputRow>();
candidate.ScheduledRolesJson = "[\"designated\",\"service\"]";
candidate.PayrollRole = null;
Check(SettlementAttendanceSync.MergeMissingInputs("run", ambiguous, [candidate]).Single().Code == "PAYROLL_ROLE_UNRESOLVED" && ambiguous.Count == 0,
    "Multi-role attendance requires review rather than paying the same hour twice.");
candidate.ScheduledRolesJson = "[\"designated\"]";
candidate.PayrollRole = "designated";
candidate.ActualMinutes = 0;
SettlementAttendanceSync.MergeMissingInputs("run", ambiguous, [candidate]);
Check(ambiguous.Count == 0, "A plan without effective attendance does not generate wages.");

var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var source = new SettlementSourceData { StaffInputs = inputs, PayrollAssignments = [candidate] };
var result = calculate.Invoke(new SettlementService(null!, null!, null!, null!),
    [new SettlementRunRow { Id = "run" }, new SettlementRuleRow { DesignatedHourlyRate = 30000, TimeRoundMinutes = 30 }, source])!;
var lines = (IReadOnlyList<SettlementResultLineRow>)result.GetType().GetProperty("Results")!.GetValue(result)!;
Check(lines.Single(x => x.StaffId == "staff" && x.Role == "designated").BasePay == 30000, "A synchronized one-hour designated shift earns its base wage even without order revenue.");

foreach (var frontRole in new[] { "designated", "service", "manager" })
{
    var dual = new List<SettlementStaffInputRow>();
    var dualCandidate = new SettlementAttendanceCandidateRow
    {
        StaffId = "dual", DisplayName = "兼任人員", ActualMinutes = 52,
        PayrollRole = frontRole, PayrollSnapshotVersion = 1, CanUseBackstage = true, BackstageParticipant = true,
        ScheduledRolesJson = $"[\"backstage\",\"{frontRole}\"]"
    };
    Check(SettlementAttendanceSync.MergeMissingInputs("run", dual, [dualCandidate]).Count == 0,
        "Backstage plus one front role is unambiguous.");
    Check(dual.Count == 2 && dual.Single(x => x.Role == frontRole).ActualMinutes == 52
        && dual.Single(x => x.Role == "backstage").IsBackstageParticipant,
        "The front role receives attendance and backstage receives participation.");
    SettlementAttendanceSync.MergeMissingInputs("run", dual, [dualCandidate]);
    Check(dual.Count == 2, "Rechecking a concurrent backstage role is idempotent.");
    dual.Single(x => x.Role == "backstage").IsBackstageParticipant = false;
    SettlementAttendanceSync.MergeMissingInputs("run", dual, [dualCandidate]);
    Check(!dual.Single(x => x.Role == "backstage").IsBackstageParticipant, "An explicit backstage opt-out is preserved.");
    dual.Single(x => x.Role == "backstage").IsBackstageParticipant = true;
    var dualResult = calculate.Invoke(new SettlementService(null!, null!, null!, null!),
        [new SettlementRunRow { Id = "run", PublicTipAmount = 52 },
         new SettlementRuleRow { DesignatedHourlyRate = 30000, ServiceManagerHourlyRate = 50000,
             BackstageHourlyRate = 100000, TimeRoundMinutes = 30 },
         new SettlementSourceData { StaffInputs = dual, PayrollAssignments = [dualCandidate] }])!;
    var dualLines = (IReadOnlyList<SettlementResultLineRow>)dualResult.GetType().GetProperty("Results")!.GetValue(dualResult)!;
    Check(dualLines.Single(x => x.Role == "backstage").BasePay == 0
        && dualLines.Single(x => x.Role == "backstage").PayableHours == 0,
        "Backstage participation does not duplicate front-of-house hourly pay.");
    Check(dualLines.Sum(x => x.PublicTip) == 52, "Public tips are distributed once through the front role.");
    var existingBackstage = new List<SettlementStaffInputRow> { dual.Single(x => x.Role == "backstage") };
    Check(SettlementAttendanceSync.MergeMissingInputs("run", existingBackstage, [dualCandidate]).Count == 0
        && existingBackstage.Count == 2, "A saved backstage input does not block a missing approved front role.");
}
Console.WriteLine("Attendance settlement synchronization regressions passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
