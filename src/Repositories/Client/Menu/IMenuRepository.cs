using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Menu;

public interface IMenuRepository
{
    Task<IReadOnlyList<PricingRuleRow>> GetPricingRulesAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyList<MenuCategoryRow> Categories, IReadOnlyList<MenuItemRow> Items, IReadOnlyList<MenuSetRow> Sets, IReadOnlyList<MenuSetItemRow> SetItems)> GetMenuAsync(CancellationToken cancellationToken);
}
