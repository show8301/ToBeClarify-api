using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Menu;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/menu")]
public sealed class MenuController : ControllerBase
{
    private readonly IMenuService _service;
    public MenuController(IMenuService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MenuDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MenuDto>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<MenuDto>.Ok(await _service.GetMenuAsync(cancellationToken)));

    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MenuCategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MenuCategoryDto>>>> GetCategories(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<MenuCategoryDto>>.Ok((await _service.GetMenuAsync(cancellationToken)).Categories));

    [HttpGet("items/{id}")]
    [ProducesResponseType(typeof(ApiResponse<MenuItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MenuItemDto>>> GetItem(string id, CancellationToken cancellationToken)
    {
        var item = (await _service.GetMenuAsync(cancellationToken)).Categories.SelectMany(category => category.Items)
            .FirstOrDefault(value => value.Id == id);
        return item is null
            ? NotFound(ApiResponse<object>.Fail("MENU_ITEM_NOT_FOUND", "Menu item not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<MenuItemDto>.Ok(item));
    }

    [HttpGet("sets/{id}")]
    [ProducesResponseType(typeof(ApiResponse<MenuSetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MenuSetDto>>> GetSet(string id, CancellationToken cancellationToken)
    {
        var item = (await _service.GetMenuAsync(cancellationToken)).Sets.FirstOrDefault(value => value.Id == id);
        return item is null
            ? NotFound(ApiResponse<object>.Fail("MENU_SET_NOT_FOUND", "Menu set not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<MenuSetDto>.Ok(item));
    }
}
