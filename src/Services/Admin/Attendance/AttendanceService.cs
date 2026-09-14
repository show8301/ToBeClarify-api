using System.Globalization;
using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Admin.Attendance;

namespace ToBeClarify.Api.Services.Admin.Attendance;

public sealed class AttendanceService(AttendanceRepository repository, IAppClock clock)
{
    public async Task<StaffAttendanceOverviewDto> GetOverviewAsync(string? businessDate, ClaimsPrincipal actor, CancellationToken ct)
    {
        var date = ParseDate(businessDate) ?? DateOnly.FromDateTime(clock.LocalDateTime);
        var staffId = CanManageAll(actor) ? null : OwnStaffId(actor);
        return await repository.GetOverviewAsync(date, staffId, clock.LocalDateTime, ct);
    }

    public async Task<StaffAttendanceEventDto> ApplyAsync(StaffAttendanceActionRequest request,
        ClaimsPrincipal actor, CancellationToken ct)
    {
        var date = ParseDate(request.BusinessDate) ?? throw new BusinessException("出勤日期無效。", "ATTENDANCE_DATE_INVALID");
        if (!Guid.TryParse(request.OperationId, out var op)) throw new BusinessException("操作識別碼無效。", "ATTENDANCE_OPERATION_INVALID");
        var requestedStaff = string.IsNullOrWhiteSpace(request.StaffId) ? null : request.StaffId.Trim();
        var staffId = CanManageAll(actor) ? requestedStaff ?? OwnStaffId(actor) : OwnStaffId(actor);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > 500) throw new BusinessException("原因最多 500 字。", "ATTENDANCE_REASON_TOO_LONG");
        var normalized = new StaffAttendanceActionRequest
        {
            OperationId = op.ToString(), BusinessDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StaffId = staffId, Action = request.Action.Trim().ToLowerInvariant(), OccurredAt = request.OccurredAt,
            EndedAt = request.EndedAt, Minutes = request.Minutes, Reason = reason
        };
        return await repository.ApplyAsync(staffId, normalized, clock.LocalDateTime, ActorId(actor), ct);
    }

    private static DateOnly? ParseDate(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new BusinessException("出勤日期必須使用 yyyy-MM-dd。", "ATTENDANCE_DATE_INVALID");

    private static bool CanManageAll(ClaimsPrincipal actor)
        => actor.IsInRole("manager") || actor.IsInRole("developer") || actor.FindFirstValue(AdminAuthConstants.RoleClaimType) is "manager" or "developer";
    private static string OwnStaffId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType)
            ?? throw new ForbiddenException("此帳號尚未連結店員身分。", "STAFF_ACCOUNT_NOT_LINKED");
    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedException();
}
