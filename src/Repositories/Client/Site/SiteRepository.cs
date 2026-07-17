using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Site;

public sealed class SiteRepository : DapperRepositoryBase, ISiteRepository
{
    public SiteRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<SiteSettingRow>> GetSiteSettingsAsync(string? settingKey, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `SETTING_KEY` AS SettingKey, `SETTING_VALUE` AS SettingValue, `DESCRIPTION` AS Description
            FROM `SITE_SETTINGS`
            WHERE `IS_ACTIVE` = TRUE AND (@SettingKey IS NULL OR `SETTING_KEY` = @SettingKey)
            ORDER BY `SETTING_KEY`;
            """;
        return await QueryAsync<SiteSettingRow>(sql, new { SettingKey = settingKey }, cancellationToken);
    }

    public async Task<IReadOnlyList<NavigationItemRow>> GetNavigationItemsAsync(string placement, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `LABEL` AS Label, `ROUTE_PATH` AS RoutePath, `PLACEMENT` AS Placement,
                   `PARENT_ITEM_ID` AS ParentItemId, `IS_DROPDOWN` AS IsDropdown, `SORT_ORDER` AS SortOrder
            FROM `NAVIGATION_ITEMS`
            WHERE `IS_ENABLED` = TRUE AND (`PLACEMENT` = @Placement OR `PLACEMENT` = 'both')
            ORDER BY `SORT_ORDER`, `LABEL`;
            """;
        return await QueryAsync<NavigationItemRow>(sql, new { Placement = placement }, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopRuleRow>> GetShopRulesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `RULE_TEXT` AS RuleText, `RULE_NOTE` AS RuleNote
            FROM `SHOP_RULES` WHERE `IS_ENABLED` = TRUE ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """;
        return await QueryAsync<ShopRuleRow>(sql, null, cancellationToken);
    }
}
