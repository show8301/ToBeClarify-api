using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Menu;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/pricing-rules")]
public sealed class PricingRulesController : ControllerBase
{
    private readonly IMenuService _service;
    public PricingRulesController(IMenuService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PricingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PricingRuleDto>>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<PricingRuleDto>>.Ok(await _service.GetPricingRulesAsync(cancellationToken)));
}
