using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/orders")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>Admin orders endpoint placeholder.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<string>> GetOrders()
    {
        return Ok(ApiResponse<string>.Ok("Admin orders API placeholder"));
    }
}
