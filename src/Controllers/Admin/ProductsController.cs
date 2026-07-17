using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/products")]
public sealed class ProductsController : ControllerBase
{
    /// <summary>Admin products endpoint placeholder.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<string>> GetProducts()
    {
        return Ok(ApiResponse<string>.Ok("Admin products API placeholder"));
    }
}
