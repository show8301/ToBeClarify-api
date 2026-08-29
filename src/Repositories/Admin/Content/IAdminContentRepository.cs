using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Content;

public interface IAdminContentRepository
{
    Task<IReadOnlyList<AdminSiteSettingRow>> GetSiteSettingsAsync(CancellationToken cancellationToken);
    Task<AdminSiteSettingRow?> GetSiteSettingAsync(string settingKey, CancellationToken cancellationToken);
    Task UpsertSiteSettingAsync(string id, string settingKey, SaveSiteSettingRequest request, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminNavigationItemRow>> GetNavigationItemsAsync(CancellationToken cancellationToken);
    Task UpsertNavigationItemAsync(string id, SaveNavigationItemRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteNavigationItemAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminHomeCarouselRow>> GetHomeCarouselsAsync(CancellationToken cancellationToken);
    Task UpsertHomeCarouselAsync(string id, SaveHomeCarouselRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteHomeCarouselAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminHomeSlideRow>> GetHomeSlidesAsync(CancellationToken cancellationToken);
    Task<AdminHomeSlideRow?> GetHomeSlideAsync(string id, CancellationToken cancellationToken);
    Task UpsertHomeSlideAsync(string id, SaveHomeSlideRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteHomeSlideAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminShopRuleRow>> GetShopRulesAsync(CancellationToken cancellationToken);
    Task UpsertShopRuleAsync(string id, SaveShopRuleRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteShopRuleAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminStaffMemberListRow>> GetStaffMembersAsync(CancellationToken cancellationToken);
    Task<AdminStaffMemberRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminStaffServiceRow>> GetStaffServicesAsync(string staffId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminStaffGalleryItemRow>> GetStaffGalleryAsync(string staffId, CancellationToken cancellationToken);
    Task SaveStaffMemberAsync(string id, SaveStaffMemberRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task UpdateStaffMemberStatusAsync(string id, bool? isWorkingToday, bool? isActive, string actorId, DateTime now, CancellationToken cancellationToken);
    Task ReorderStaffMembersAsync(IReadOnlyList<ReorderStaffMemberItem> items, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<bool> StaffMemberHasAdminAccountAsync(string id, CancellationToken cancellationToken);
    Task DeleteStaffMemberAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminGalleryAlbumRow>> GetGalleryAlbumsAsync(CancellationToken cancellationToken);
    Task<AdminGalleryAlbumRow?> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminGalleryItemRow>> GetGalleryItemsAsync(string albumId, CancellationToken cancellationToken);
    Task UpsertGalleryAlbumAsync(string id, SaveGalleryAlbumRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteGalleryAlbumAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminPricingRuleRow>> GetPricingRulesAsync(CancellationToken cancellationToken);
    Task UpsertPricingRuleAsync(string id, SavePricingRuleRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeletePricingRuleAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AdminMenuCategoryRow> Categories, IReadOnlyList<AdminMenuItemRow> Items,
        IReadOnlyList<AdminMenuSetRow> Sets, IReadOnlyList<AdminMenuSetItemRow> SetItems)> GetMenuAsync(CancellationToken cancellationToken);
    Task UpsertMenuCategoryAsync(string id, SaveMenuCategoryRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteMenuCategoryAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);
    Task UpsertMenuItemAsync(string id, SaveMenuItemRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteMenuItemAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);
    Task SaveMenuSetAsync(string id, SaveMenuSetRequest request, string actorId, DateTime now, CancellationToken cancellationToken);
    Task DeleteMenuSetAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken);
}
