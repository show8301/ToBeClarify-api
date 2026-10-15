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
            if (candidate.ActualMinutes <= 0) continue;
            var existing = inputs.Where(x => x.StaffId == candidate.StaffId).ToArray();
            string[] roles;
            try
            {
                roles = (JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(candidate.ScheduledRolesJson)
                        ? "[]" : candidate.ScheduledRolesJson) ?? [])
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            catch (JsonException) { roles = []; }
            if (roles.Length == 0 || roles.Any(role => role is not ("designated" or "service" or "manager" or "backstage")))
            {
                if (existing.Length == 0) AddRoleAnomaly();
                continue;
            }

            // Backstage is a participation share, independent of the front-of-house hourly role.
            // Existing role rows (including an explicit opt-out) are never overwritten here.
            var hourlyRoles = roles.Where(role => role != "backstage").ToArray();
            if (!existing.Any(x => x.Role != "backstage"))
            {
                if (hourlyRoles.Length == 1) AddInput(hourlyRoles[0]);
                else if (hourlyRoles.Length > 1) AddRoleAnomaly();
            }
            if (roles.Contains("backstage") && !existing.Any(x => x.Role == "backstage")) AddInput("backstage");

            void AddRoleAnomaly() => anomalies.Add(new SettlementAnomalyDto("ATTENDANCE_ROLE_UNRESOLVED",
                $"{candidate.DisplayName}已有 {candidate.ActualMinutes} 分鐘出勤，但前台計薪角色無法唯一確認；請核對指名、服務生或經理的工時分配後保存結算輸入。幕後參與可獨立兼任。",
                candidate.StaffId, candidate.DisplayName));

            void AddInput(string role) => inputs.Add(new SettlementStaffInputRow
            {
                SettlementId = settlementId, StaffId = candidate.StaffId,
                DisplayName = candidate.DisplayName, RoleTitle = candidate.RoleTitle,
                IsActive = candidate.IsActive, IsWorking = true, Role = role,
                ActualMinutes = candidate.ActualMinutes, AttendanceSource = "clock",
                PublicTipEligible = role != "backstage", IsBackstageParticipant = role == "backstage"
            });
        }
        return anomalies;
    }
}
