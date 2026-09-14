using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController, Authorize(Policy="AdminOnly"), Route("api/admin/business-day-plans")]
public sealed class BusinessDayPlansController(BusinessDayPlanService service, IBusinessDayContext context):ControllerBase
{
    [HttpGet("{date}")]
    public async Task<IActionResult> Get(DateOnly date,CancellationToken ct)=>Ok(ApiResponse<BusinessDayPlanDto>.Ok(await service.GetAsync(date,ct)));
    [HttpGet("{date}/context")]
    public async Task<IActionResult> Context(DateOnly date,CancellationToken ct)=>Ok(ApiResponse<BusinessDayContext>.Ok(await context.GetForDateAsync(date,ct)));
    [HttpPut("{date}"),Authorize(Policy="AdminManager")]
    public async Task<IActionResult> Save(DateOnly date,SaveBusinessDayPlanRequest request,CancellationToken ct)
        =>Ok(ApiResponse<BusinessDayPlanDto>.Ok(await service.SaveAsync(date,request,User.FindFirstValue(ToBeClarify.Api.Auth.AdminAuthConstants.UserIdClaimType)??User.FindFirstValue(ClaimTypes.NameIdentifier)??"unknown",ct)));
}
