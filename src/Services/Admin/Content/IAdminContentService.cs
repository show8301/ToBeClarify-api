using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Content;

public interface IAdminContentService
{
    Task<IReadOnlyList<AdminSiteSettingDto>> GetSiteSettingsAsync(CancellationToken cancellationToken);
    Task<AdminSiteSettingDto> SaveSiteSettingAsync(string settingKey, SaveSiteSettingRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminNavigationItemDto>> GetNavigationItemsAsync(CancellationToken cancellationToken);
    Task<AdminNavigationItemDto> SaveNavigationItemAsync(string? id, SaveNavigationItemRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteNavigationItemAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminHomeCarouselDto>> GetHomeCarouselsAsync(CancellationToken cancellationToken);
    Task<AdminHomeCarouselDto> SaveHomeCarouselAsync(string? id, SaveHomeCarouselRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteHomeCarouselAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminHomeSlideDto>> GetHomeSlidesAsync(CancellationToken cancellationToken);
    Task<AdminHomeSlideDto> SaveHomeSlideAsync(string? id, SaveHomeSlideRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteHomeSlideAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminShopRuleDto>> GetShopRulesAsync(CancellationToken cancellationToken);
    Task<AdminShopRuleDto> SaveShopRuleAsync(string? id, SaveShopRuleRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteShopRuleAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminStaffMemberDto>> GetStaffMembersAsync(ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<AdminStaffMemberDto> GetStaffMemberAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<AdminStaffMemberDto> SaveStaffMemberAsync(string id, SaveStaffMemberRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task ReorderStaffMembersAsync(ReorderStaffMembersRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteStaffMemberAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminGalleryAlbumDto>> GetGalleryAlbumsAsync(CancellationToken cancellationToken);
    Task<AdminGalleryAlbumDto> SaveGalleryAlbumAsync(string? id, SaveGalleryAlbumRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteGalleryAlbumAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminPricingRuleDto>> GetPricingRulesAsync(CancellationToken cancellationToken);
    Task<AdminPricingRuleDto> SavePricingRuleAsync(string? id, SavePricingRuleRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeletePricingRuleAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);

    Task<AdminMenuDto> GetMenuAsync(CancellationToken cancellationToken);
    Task<AdminMenuCategoryDto> SaveMenuCategoryAsync(string? id, SaveMenuCategoryRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteMenuCategoryAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<AdminMenuItemDto> SaveMenuItemAsync(string? id, SaveMenuItemRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteMenuItemAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<AdminMenuSetDto> SaveMenuSetAsync(string? id, SaveMenuSetRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DeleteMenuSetAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken);
}
