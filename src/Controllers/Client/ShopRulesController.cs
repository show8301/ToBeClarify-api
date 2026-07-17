using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Site;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/shop-rules")]
public sealed class ShopRulesController : ControllerBase
{
    private readonly ISiteService _service;
    public ShopRulesController(ISiteService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ShopRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ShopRuleDto>>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<ShopRuleDto>>.Ok(await _service.GetShopRulesAsync(cancellationToken)));
}
