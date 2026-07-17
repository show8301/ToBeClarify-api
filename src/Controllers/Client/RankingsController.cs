using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Rankings;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/rankings")]
public sealed class RankingsController : ControllerBase
{
    private readonly IRankingService _service;
    public RankingsController(IRankingService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RankingDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RankingDto>>>> Get(
        [FromQuery] string type, [FromQuery] string? period, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<RankingDto>>.Ok(await _service.GetRankingsAsync(type, period, cancellationToken)));
}
