using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Site;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/site-settings")]
public sealed class SiteSettingsController : ControllerBase
{
    private readonly ISiteService _service;
    public SiteSettingsController(ISiteService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SiteSettingDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SiteSettingDto>>>> GetAll(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<SiteSettingDto>>.Ok(await _service.GetSiteSettingsAsync(cancellationToken)));

    [HttpGet("{settingKey}")]
    [ProducesResponseType(typeof(ApiResponse<SiteSettingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SiteSettingDto>>> GetOne(string settingKey, CancellationToken cancellationToken)
        => Ok(ApiResponse<SiteSettingDto>.Ok(await _service.GetSiteSettingAsync(settingKey, cancellationToken)));
}
