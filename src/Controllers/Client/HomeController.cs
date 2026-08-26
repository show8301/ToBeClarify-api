using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Home;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/home")]
public sealed class HomeController : ControllerBase
{
    private readonly IHomeService _service;
    public HomeController(IHomeService service) => _service = service;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<HomeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<HomeDto>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<HomeDto>.Ok(await _service.GetHomeAsync(cancellationToken)));
}
