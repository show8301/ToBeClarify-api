using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Menu;

public interface IMenuService
{
    Task<IReadOnlyList<PricingRuleDto>> GetPricingRulesAsync(CancellationToken cancellationToken);
    Task<MenuDto> GetMenuAsync(CancellationToken cancellationToken);
}
