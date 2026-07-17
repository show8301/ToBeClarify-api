using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Site;

public interface ISiteRepository
{
    Task<IReadOnlyList<SiteSettingRow>> GetSiteSettingsAsync(string? settingKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<NavigationItemRow>> GetNavigationItemsAsync(string placement, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopRuleRow>> GetShopRulesAsync(CancellationToken cancellationToken);
}
