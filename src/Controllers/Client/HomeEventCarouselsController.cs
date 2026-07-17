using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Home;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/home-event-carousels")]
public sealed class HomeEventCarouselsController : ControllerBase
{
    private readonly IHomeService _service;
    public HomeEventCarouselsController(IHomeService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HomeEventCarouselDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HomeEventCarouselDto>>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<HomeEventCarouselDto>>.Ok(await _service.GetCarouselsAsync(cancellationToken)));
}
