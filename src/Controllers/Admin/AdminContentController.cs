using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Content;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public sealed class AdminContentController : ControllerBase
{
    private readonly IAdminContentService _service;

    public AdminContentController(IAdminContentService service) => _service = service;

    [HttpGet("site-settings")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminSiteSettingDto>>>> GetSiteSettings(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminSiteSettingDto>>.Ok(await _service.GetSiteSettingsAsync(cancellationToken)));

    [HttpPut("site-settings/{settingKey}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminSiteSettingDto>>> SaveSiteSetting(string settingKey, SaveSiteSettingRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminSiteSettingDto>.Ok(await _service.SaveSiteSettingAsync(settingKey, request, User, cancellationToken)));

    [HttpGet("navigation-items")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminNavigationItemDto>>>> GetNavigationItems(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminNavigationItemDto>>.Ok(await _service.GetNavigationItemsAsync(cancellationToken)));

    [HttpPost("navigation-items")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminNavigationItemDto>>> CreateNavigationItem(SaveNavigationItemRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminNavigationItemDto>.Ok(await _service.SaveNavigationItemAsync(null, request, User, cancellationToken)));

    [HttpPut("navigation-items/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminNavigationItemDto>>> UpdateNavigationItem(string id, SaveNavigationItemRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminNavigationItemDto>.Ok(await _service.SaveNavigationItemAsync(id, request, User, cancellationToken)));

    [HttpDelete("navigation-items/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteNavigationItem(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteNavigationItemAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("home-event-carousels")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminHomeCarouselDto>>>> GetHomeCarousels(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminHomeCarouselDto>>.Ok(await _service.GetHomeCarouselsAsync(cancellationToken)));

    [HttpPost("home-event-carousels")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminHomeCarouselDto>>> CreateHomeCarousel(SaveHomeCarouselRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminHomeCarouselDto>.Ok(await _service.SaveHomeCarouselAsync(null, request, User, cancellationToken)));

    [HttpPut("home-event-carousels/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminHomeCarouselDto>>> UpdateHomeCarousel(string id, SaveHomeCarouselRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminHomeCarouselDto>.Ok(await _service.SaveHomeCarouselAsync(id, request, User, cancellationToken)));

    [HttpDelete("home-event-carousels/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteHomeCarousel(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteHomeCarouselAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("home-slides")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminHomeSlideDto>>>> GetHomeSlides(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminHomeSlideDto>>.Ok(await _service.GetHomeSlidesAsync(cancellationToken)));

    [HttpPost("home-slides")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminHomeSlideDto>>> CreateHomeSlide(SaveHomeSlideRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminHomeSlideDto>.Ok(await _service.SaveHomeSlideAsync(null, request, User, cancellationToken)));

    [HttpPut("home-slides/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminHomeSlideDto>>> UpdateHomeSlide(string id, SaveHomeSlideRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminHomeSlideDto>.Ok(await _service.SaveHomeSlideAsync(id, request, User, cancellationToken)));

    [HttpDelete("home-slides/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteHomeSlide(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteHomeSlideAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("shop-rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminShopRuleDto>>>> GetShopRules(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminShopRuleDto>>.Ok(await _service.GetShopRulesAsync(cancellationToken)));

    [HttpPost("shop-rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminShopRuleDto>>> CreateShopRule(SaveShopRuleRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminShopRuleDto>.Ok(await _service.SaveShopRuleAsync(null, request, User, cancellationToken)));

    [HttpPut("shop-rules/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminShopRuleDto>>> UpdateShopRule(string id, SaveShopRuleRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminShopRuleDto>.Ok(await _service.SaveShopRuleAsync(id, request, User, cancellationToken)));

    [HttpDelete("shop-rules/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteShopRule(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteShopRuleAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("staff-members")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminStaffMemberListItemDto>>>> GetStaffMembers(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminStaffMemberListItemDto>>.Ok(await _service.GetStaffMembersAsync(User, cancellationToken)));

    [HttpGet("staff-members/{id}")]
    public async Task<ActionResult<ApiResponse<AdminStaffMemberDto>>> GetStaffMember(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminStaffMemberDto>.Ok(await _service.GetStaffMemberAsync(id, User, cancellationToken)));

    [HttpPut("staff-members/{id}")]
    public async Task<ActionResult<ApiResponse<AdminStaffMemberDto>>> UpdateStaffMember(string id, SaveStaffMemberRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminStaffMemberDto>.Ok(await _service.SaveStaffMemberAsync(id, request, User, cancellationToken)));

    [HttpPut("staff-members/{id}/status")]
    public async Task<ActionResult<ApiResponse<AdminStaffMemberDto>>> UpdateStaffMemberStatus(string id, UpdateStaffMemberStatusRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminStaffMemberDto>.Ok(await _service.UpdateStaffMemberStatusAsync(id, request, User, cancellationToken)));

    [HttpPut("staff-members/order")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> ReorderStaffMembers(ReorderStaffMembersRequest request, CancellationToken cancellationToken)
    {
        await _service.ReorderStaffMembersAsync(request, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("staff-members/{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteStaffMember(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteStaffMemberAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("gallery-albums")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminGalleryAlbumDto>>>> GetGalleryAlbums(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminGalleryAlbumDto>>.Ok(await _service.GetGalleryAlbumsAsync(cancellationToken)));

    [HttpPost("gallery-albums")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminGalleryAlbumDto>>> CreateGalleryAlbum(SaveGalleryAlbumRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminGalleryAlbumDto>.Ok(await _service.SaveGalleryAlbumAsync(null, request, User, cancellationToken)));

    [HttpPut("gallery-albums/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminGalleryAlbumDto>>> UpdateGalleryAlbum(string id, SaveGalleryAlbumRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminGalleryAlbumDto>.Ok(await _service.SaveGalleryAlbumAsync(id, request, User, cancellationToken)));

    [HttpDelete("gallery-albums/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteGalleryAlbum(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteGalleryAlbumAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("pricing-rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminPricingRuleDto>>>> GetPricingRules(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminPricingRuleDto>>.Ok(await _service.GetPricingRulesAsync(cancellationToken)));

    [HttpPost("pricing-rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminPricingRuleDto>>> CreatePricingRule(SavePricingRuleRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminPricingRuleDto>.Ok(await _service.SavePricingRuleAsync(null, request, User, cancellationToken)));

    [HttpPut("pricing-rules/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminPricingRuleDto>>> UpdatePricingRule(string id, SavePricingRuleRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminPricingRuleDto>.Ok(await _service.SavePricingRuleAsync(id, request, User, cancellationToken)));

    [HttpDelete("pricing-rules/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeletePricingRule(string id, CancellationToken cancellationToken)
    {
        await _service.DeletePricingRuleAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("menu")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuDto>>> GetMenu(CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuDto>.Ok(await _service.GetMenuAsync(cancellationToken)));

    [HttpPost("menu/categories")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuCategoryDto>>> CreateMenuCategory(SaveMenuCategoryRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuCategoryDto>.Ok(await _service.SaveMenuCategoryAsync(null, request, User, cancellationToken)));

    [HttpPut("menu/categories/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuCategoryDto>>> UpdateMenuCategory(string id, SaveMenuCategoryRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuCategoryDto>.Ok(await _service.SaveMenuCategoryAsync(id, request, User, cancellationToken)));

    [HttpDelete("menu/categories/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMenuCategory(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteMenuCategoryAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("menu/items")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuItemDto>>> CreateMenuItem(SaveMenuItemRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuItemDto>.Ok(await _service.SaveMenuItemAsync(null, request, User, cancellationToken)));

    [HttpPut("menu/items/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuItemDto>>> UpdateMenuItem(string id, SaveMenuItemRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuItemDto>.Ok(await _service.SaveMenuItemAsync(id, request, User, cancellationToken)));

    [HttpDelete("menu/items/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMenuItem(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteMenuItemAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("menu/sets")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuSetDto>>> CreateMenuSet(SaveMenuSetRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuSetDto>.Ok(await _service.SaveMenuSetAsync(null, request, User, cancellationToken)));

    [HttpPut("menu/sets/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdminMenuSetDto>>> UpdateMenuSet(string id, SaveMenuSetRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMenuSetDto>.Ok(await _service.SaveMenuSetAsync(id, request, User, cancellationToken)));

    [HttpDelete("menu/sets/{id}")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMenuSet(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteMenuSetAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
