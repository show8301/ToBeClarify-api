using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Attendance;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/attendance")]
public sealed class AttendanceController(AttendanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<StaffAttendanceOverviewDto>>> Get(
        [FromQuery] string? businessDate, CancellationToken ct)
        => Ok(ApiResponse<StaffAttendanceOverviewDto>.Ok(await service.GetOverviewAsync(businessDate, User, ct)));

    [HttpPost("actions")]
    public async Task<ActionResult<ApiResponse<StaffAttendanceEventDto>>> Apply(
        StaffAttendanceActionRequest request, CancellationToken ct)
        => Ok(ApiResponse<StaffAttendanceEventDto>.Ok(await service.ApplyAsync(request, User, ct)));
}
