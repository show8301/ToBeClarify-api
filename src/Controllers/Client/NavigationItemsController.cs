using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Site;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/navigation-items")]
public sealed class NavigationItemsController : ControllerBase
{
    private readonly ISiteService _service;
    public NavigationItemsController(ISiteService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NavigationItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NavigationItemDto>>>> Get(
        [FromQuery] string placement = "navbar", CancellationToken cancellationToken = default)
        => Ok(ApiResponse<IReadOnlyList<NavigationItemDto>>.Ok(await _service.GetNavigationAsync(placement, cancellationToken)));
}
