using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Site;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Site;

public sealed class SiteService : ISiteService
{
    private readonly ISiteRepository _repository;

    public SiteService(ISiteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<SiteSettingDto>> GetSiteSettingsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetSiteSettingsAsync(null, cancellationToken);
        return rows.Select(ClientContentMappings.MapSetting).ToArray();
    }

    public async Task<SiteSettingDto> GetSiteSettingAsync(string settingKey, CancellationToken cancellationToken)
    {
        var key = ClientContentMappings.RequiredValue(settingKey, "SETTING_KEY_REQUIRED", "Setting key is required.");
        var row = (await _repository.GetSiteSettingsAsync(key, cancellationToken)).SingleOrDefault()
            ?? throw new NotFoundException("Site setting not found.", "SITE_SETTING_NOT_FOUND");
        return ClientContentMappings.MapSetting(row);
    }

    public async Task<IReadOnlyList<NavigationItemDto>> GetNavigationAsync(string placement, CancellationToken cancellationToken)
    {
        if (!ClientContentMappings.NavigationPlacements.Contains(placement))
            throw new BusinessException("Placement must be navbar or footer.", "INVALID_NAVIGATION_PLACEMENT");

        var rows = await _repository.GetNavigationItemsAsync(placement.ToLowerInvariant(), cancellationToken);
        var byId = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var childrenByParent = rows.Where(row => row.ParentItemId is not null)
            .GroupBy(row => row.ParentItemId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var roots = rows.Where(row => row.ParentItemId is null || !byId.ContainsKey(row.ParentItemId)).ToArray();
        return roots.Select(row => ClientContentMappings.BuildNavigation(row, childrenByParent, new HashSet<string>(StringComparer.Ordinal))).ToArray();
    }

    public async Task<IReadOnlyList<ShopRuleDto>> GetShopRulesAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetShopRulesAsync(cancellationToken);
        return rows.Select(row => new ShopRuleDto(row.Id, row.RuleText, row.RuleNote)).ToArray();
    }
}
