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
    StaffId = "staff", DisplayName = "三月五日", ScheduledRolesJson = "[\"designated\"]",
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
SettlementAttendanceSync.MergeMissingInputs("run", inputs, [candidate]);
Check(inputs.Count == 1 && inputs[0].Role == "designated" && inputs[0].ActivityHours == 2 && inputs[0].Note == "manager allocation",
    "A role change in the plan cannot overwrite an explicit payroll allocation.");
var ambiguous = new List<SettlementStaffInputRow>();
candidate.ScheduledRolesJson = "[\"designated\",\"service\"]";
Check(SettlementAttendanceSync.MergeMissingInputs("run", ambiguous, [candidate]).Single().Code == "ATTENDANCE_ROLE_UNRESOLVED" && ambiguous.Count == 0,
    "Multi-role attendance requires review rather than paying the same hour twice.");
candidate.ScheduledRolesJson = "[\"designated\"]";
candidate.ActualMinutes = 0;
SettlementAttendanceSync.MergeMissingInputs("run", ambiguous, [candidate]);
Check(ambiguous.Count == 0, "A plan without effective attendance does not generate wages.");

var calculate = typeof(SettlementService).GetMethod("Calculate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var source = new SettlementSourceData { StaffInputs = inputs };
var result = calculate.Invoke(new SettlementService(null!, null!, null!, null!),
    [new SettlementRunRow { Id = "run" }, new SettlementRuleRow { DesignatedHourlyRate = 30000, TimeRoundMinutes = 30 }, source])!;
var lines = (IReadOnlyList<SettlementResultLineRow>)result.GetType().GetProperty("Results")!.GetValue(result)!;
Check(lines.Single(x => x.StaffId == "staff").BasePay == 30000, "A synchronized one-hour designated shift earns its base wage even without order revenue.");
Console.WriteLine("Attendance settlement synchronization regressions passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
