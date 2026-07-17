using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Site;

public interface ISiteService
{
    Task<IReadOnlyList<SiteSettingDto>> GetSiteSettingsAsync(CancellationToken cancellationToken);
    Task<SiteSettingDto> GetSiteSettingAsync(string settingKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<NavigationItemDto>> GetNavigationAsync(string placement, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopRuleDto>> GetShopRulesAsync(CancellationToken cancellationToken);
}
