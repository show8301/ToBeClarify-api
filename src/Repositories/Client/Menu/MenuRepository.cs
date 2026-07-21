using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Menu;

public sealed class MenuRepository : DapperRepositoryBase, IMenuRepository
{
    public MenuRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<PricingRuleRow>> GetPricingRulesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `TITLE` AS Title, `DESCRIPTION` AS Description, `PRICE_TEXT` AS PriceText
            FROM `PRICING_RULES` WHERE `IS_ENABLED` = TRUE ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """;
        return await QueryAsync<PricingRuleRow>(sql, null, cancellationToken);
    }

    public async Task<(IReadOnlyList<MenuCategoryRow> Categories, IReadOnlyList<MenuItemRow> Items, IReadOnlyList<MenuSetRow> Sets, IReadOnlyList<MenuSetItemRow> SetItems)> GetMenuAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `CATEGORY_NAME` AS CategoryName, `CATEGORY_DESCRIPTION` AS CategoryDescription
            FROM `MENU_CATEGORIES` WHERE `IS_ENABLED` = TRUE ORDER BY `SORT_ORDER`, `CATEGORY_NAME`;

            SELECT I.`ID` AS Id, I.`CATEGORY_ID` AS CategoryId, I.`ITEM_NAME` AS ItemName,
                   I.`ITEM_DESCRIPTION` AS ItemDescription, I.`PRICE` AS Price,
                   I.`MEDIA_ID` AS MediaId, I.`IMAGE_URL` AS LegacyImageUrl, I.`TAGS` AS Tags
            FROM `MENU_ITEMS` I
            INNER JOIN `MENU_CATEGORIES` C ON C.`ID` = I.`CATEGORY_ID` AND C.`IS_ENABLED` = TRUE
            WHERE I.`IS_AVAILABLE` = TRUE ORDER BY C.`SORT_ORDER`, I.`SORT_ORDER`, I.`ITEM_NAME`;

            SELECT `ID` AS Id, `SET_NAME` AS SetName, `SET_DESCRIPTION` AS SetDescription,
                   `SET_PRICE` AS SetPrice, `MEDIA_ID` AS MediaId, `IMAGE_URL` AS LegacyImageUrl
            FROM `MENU_SETS` WHERE `IS_AVAILABLE` = TRUE ORDER BY `SORT_ORDER`, `SET_NAME`;

            SELECT SI.`ID` AS Id, SI.`SET_ID` AS SetId, SI.`MENU_ITEM_ID` AS MenuItemId,
                   I.`ITEM_NAME` AS ItemName, SI.`ITEM_ROLE` AS ItemRole, SI.`QUANTITY` AS Quantity
            FROM `MENU_SET_ITEMS` SI
            INNER JOIN `MENU_SETS` S ON S.`ID` = SI.`SET_ID` AND S.`IS_AVAILABLE` = TRUE
            INNER JOIN `MENU_ITEMS` I ON I.`ID` = SI.`MENU_ITEM_ID` AND I.`IS_AVAILABLE` = TRUE
            ORDER BY S.`SORT_ORDER`, SI.`SORT_ORDER`, I.`ITEM_NAME`;
            """;

        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        var categories = (await multi.ReadAsync<MenuCategoryRow>()).AsList();
        var items = (await multi.ReadAsync<MenuItemRow>()).AsList();
        var sets = (await multi.ReadAsync<MenuSetRow>()).AsList();
        var setItems = (await multi.ReadAsync<MenuSetItemRow>()).AsList();
        return (categories, items, sets, setItems);
    }
}
