using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public static class SettlementAttendanceSync
{
    public static SettlementSourceData RefreshFinalizedSource(string settlementId, SettlementSourceData source)
    {
        var inputs = source.StaffInputs.Select(input => new SettlementStaffInputRow
        {
            Id = input.Id, SettlementId = input.SettlementId, StaffId = input.StaffId,
            CreatedAt = input.CreatedAt, CreatedBy = input.CreatedBy,
            DisplayName = input.DisplayName, RoleTitle = input.RoleTitle, IsActive = input.IsActive,
            IsWorking = input.IsWorking, Role = input.Role, PayrollSnapshotVersion = input.PayrollSnapshotVersion,
            ActualMinutes = input.ActualMinutes, AttendanceSource = input.AttendanceSource,
            AttendanceRequestId = input.AttendanceRequestId, AttendanceApprovedBy = input.AttendanceApprovedBy,
            AttendanceApprovedAt = input.AttendanceApprovedAt, ActivityHours = input.ActivityHours,
            PublicTipEligible = input.PublicTipEligible, IsBackstageParticipant = input.IsBackstageParticipant,
            Note = input.Note
        }).ToList();
        var eventsByStaff = source.AttendanceEvents.GroupBy(x => x.StaffId)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var events = eventsByStaff.GetValueOrDefault(input.StaffId, []);
            if (input.AttendanceSource != "backfill_approved" && events.Any(x => x.EventType is "clock_out" or "scheduled_end" or "manual_interval" or "adjustment"))
                input.ActualMinutes = AttendanceMinutes.Effective(events);
            if (input.AttendanceSource != "backfill_approved" && events.Any(x => x.Source is "scheduled" or "manual")) input.AttendanceSource = "clock";
        }
        var anomalies = MergeMissingInputs(settlementId, inputs, source.PayrollAssignments);
        return source with { StaffInputs = inputs, AttendanceAnomalies = anomalies };
    }

    public static IReadOnlyList<SettlementAnomalyDto> MergeMissingInputs(string settlementId,
        IList<SettlementStaffInputRow> inputs, IEnumerable<SettlementAttendanceCandidateRow> candidates)
    {
        var anomalies = new List<SettlementAnomalyDto>();
        var assignments = candidates.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
        ReconcilePayrollRoles(inputs, assignments.Values);
        foreach (var input in inputs)
        {
            if (!assignments.TryGetValue(input.StaffId, out var assignment) || assignment.PayrollRole is null)
            {
                Add(input.StaffId, input.DisplayName, "PAYROLL_ROLE_UNRESOLVED", "缺少上班計薪職位快照，請由經理在出勤區確認。");
                continue;
            }
            if (input.Role != assignment.PayrollRole && !(input.Role == "backstage" && assignment.CanUseBackstage))
                Add(input.StaffId, input.DisplayName, "PAYROLL_INPUT_CONFLICT", "既有輸入與上班計薪職位不一致，請核對並移除衝突輸入後保存。");
            if (input.Role == "backstage" && !assignment.CanUseBackstage)
                Add(input.StaffId, input.DisplayName, "BACKSTAGE_NOT_ALLOWED", "幕後參與僅限上班時具開發資格的人員。");
            if (input.PayrollSnapshotVersion != assignment.PayrollSnapshotVersion)
                Add(input.StaffId, input.DisplayName, "PAYROLL_SNAPSHOT_CHANGED", "上班計薪職位已確認或更正，請核對輸入後重新保存。");
        }
        foreach (var candidate in assignments.Values)
        {
            if (candidate.ActualMinutes <= 0) continue;
            if (candidate.PayrollRole is null)
            {
                Add(candidate.StaffId, candidate.DisplayName, "PAYROLL_ROLE_UNRESOLVED", "已有出勤但尚未確認上班計薪職位，請由經理在出勤區確認。");
                continue;
            }
            AddMissing(candidate.PayrollRole);
            if (candidate.CanUseBackstage && candidate.BackstageParticipant) AddMissing("backstage");

            void AddMissing(string role)
            {
                if (inputs.Any(x => x.StaffId == candidate.StaffId && x.Role == role)) return;
                inputs.Add(new SettlementStaffInputRow
                {
                    SettlementId = settlementId, StaffId = candidate.StaffId, DisplayName = candidate.DisplayName,
                    RoleTitle = candidate.RoleTitle, IsActive = candidate.IsActive, IsWorking = true, Role = role,
                    ActualMinutes = candidate.ActualMinutes, AttendanceSource = "clock",
                    PayrollSnapshotVersion = candidate.PayrollSnapshotVersion,
                    PublicTipEligible = role != "backstage", IsBackstageParticipant = role == "backstage"
                });
            }
        }
        return anomalies.DistinctBy(x => (x.Code, x.SourceId)).ToArray();

        void Add(string id, string name, string code, string message)
            => anomalies.Add(new SettlementAnomalyDto(code, message, id, name));
    }

    // Called only on detached calculation/input copies. Stored finalized inputs and
    // results stay intact until the manager confirms a versioned recalculation.
    public static void ReconcilePayrollRoles(IList<SettlementStaffInputRow> inputs,
        IEnumerable<SettlementAttendanceCandidateRow> candidates)
    {
        foreach (var assignment in candidates)
        {
            if (assignment.PayrollRole is null || assignment.PayrollSnapshotVersion is null) continue;
            var rows = inputs.Where(x => x.StaffId == assignment.StaffId).ToArray();
            if (rows.Length == 0 || rows.Any(x => x.PayrollSnapshotVersion >= assignment.PayrollSnapshotVersion)) continue;
            var primary = rows.Where(x => x.Role != "backstage").ToArray();
            // Ambiguous legacy inputs still require review; never silently discard wages.
            if (primary.Length > 1 || rows.Count(x => x.Role == "backstage") > 1) continue;
            var main = primary.SingleOrDefault() ?? rows.SingleOrDefault();
            if (main is null) continue;
            if (assignment.PayrollRole == "backstage")
            {
                foreach (var secondary in rows.Where(x => x != main)) inputs.Remove(secondary);
                main.IsBackstageParticipant = true;
            }
            else main.IsBackstageParticipant = false;
            main.Role = assignment.PayrollRole;
            main.PublicTipEligible = main.Role != "backstage";
            foreach (var row in inputs.Where(x => x.StaffId == assignment.StaffId))
                row.PayrollSnapshotVersion = assignment.PayrollSnapshotVersion;
        }
    }
}
