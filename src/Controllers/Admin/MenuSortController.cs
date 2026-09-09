using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController, Authorize(Policy = "AdminManager"), Route("api/admin/menu/order")]
public sealed class MenuSortController(MenuSortService service) : ControllerBase
{
    [HttpPut]
    public async Task<ActionResult<ApiResponse<bool>>> Save(MenuSortRequest request, CancellationToken ct)
    {
        await service.SortAsync(request, User.FindFirst(AdminAuthConstants.UserIdClaimType)!.Value, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
