using System.Text.Json;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public static class SettlementAttendanceSync
{
    public static IReadOnlyList<SettlementAnomalyDto> MergeMissingInputs(string settlementId,
        IList<SettlementStaffInputRow> inputs, IEnumerable<SettlementAttendanceCandidateRow> candidates)
    {
        var anomalies = new List<SettlementAnomalyDto>();
        foreach (var candidate in candidates)
        {
            // Explicit inputs retain the manager's role/participation decisions.
            if (candidate.ActualMinutes <= 0 || inputs.Any(x => x.StaffId == candidate.StaffId)) continue;
            string[] roles;
            try
            {
                roles = (JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(candidate.ScheduledRolesJson)
                        ? "[]" : candidate.ScheduledRolesJson) ?? [])
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            catch (JsonException) { roles = []; }
            if (roles.Length != 1 || roles[0] is not ("designated" or "service" or "manager" or "backstage"))
            {
                anomalies.Add(new SettlementAnomalyDto("ATTENDANCE_ROLE_UNRESOLVED",
                    $"{candidate.DisplayName}已有 {candidate.ActualMinutes} 分鐘出勤，但核准班表的計薪角色無法唯一確認；請核對角色與工時分配後保存結算輸入。",
                    candidate.StaffId, candidate.DisplayName));
                continue;
            }
            inputs.Add(new SettlementStaffInputRow
            {
                SettlementId = settlementId, StaffId = candidate.StaffId,
                DisplayName = candidate.DisplayName, RoleTitle = candidate.RoleTitle,
                IsActive = candidate.IsActive, IsWorking = true, Role = roles[0],
                ActualMinutes = candidate.ActualMinutes, AttendanceSource = "clock",
                PublicTipEligible = roles[0] != "backstage", IsBackstageParticipant = roles[0] == "backstage"
            });
        }
        return anomalies;
    }
}
