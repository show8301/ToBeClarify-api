using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public static class SettlementAttendanceSync
{
    public static IReadOnlyList<SettlementAnomalyDto> MergeMissingInputs(string settlementId,
        IList<SettlementStaffInputRow> inputs, IEnumerable<SettlementAttendanceCandidateRow> candidates)
    {
        var anomalies = new List<SettlementAnomalyDto>();
        var assignments = candidates.ToDictionary(x => x.StaffId, StringComparer.Ordinal);
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
}
