using System.Text.Json;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Client.Menu;
using ToBeClarify.Api.Repositories.Client.Site;
using ToBeClarify.Api.Services.Client.Shared;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Client.Menu;

public sealed class MenuService : IMenuService
{
    private readonly IMenuRepository _repository;
    private readonly ISiteRepository _siteRepository;
    private readonly MediaUrlService _mediaUrls;

    public MenuService(IMenuRepository repository, ISiteRepository siteRepository, MediaUrlService mediaUrls)
    {
        _repository = repository;
        _siteRepository = siteRepository;
        _mediaUrls = mediaUrls;
    }

    public async Task<IReadOnlyList<PricingRuleDto>> GetPricingRulesAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetPricingRulesAsync(cancellationToken);
        return rows.Select(ClientContentMappings.MapPricingRule).ToArray();
    }

    public async Task<MenuDto> GetMenuAsync(CancellationToken cancellationToken)
    {
        var menuTask = _repository.GetMenuAsync(cancellationToken);
        var pricingTask = _repository.GetPricingRulesAsync(cancellationToken);
        var settingsTask = _siteRepository.GetSiteSettingsAsync("menuSettings", cancellationToken);
        await Task.WhenAll(menuTask, pricingTask, settingsTask);
        var menu = await menuTask;
        var showSets = ResolveShowSets(await settingsTask);
        var itemDtos = menu.Items.Select(item => ClientContentMappings.MapMenuItem(item, _mediaUrls)).ToArray();
        var categories = menu.Categories.Select(category => new MenuCategoryDto(category.Id, category.CategoryName,
            category.CategoryDescription, itemDtos.Where(item => item.CategoryId == category.Id).ToArray())).ToArray();
        var sets = (showSets ? menu.Sets : Array.Empty<MenuSetRow>()).Select(set => new MenuSetDto(set.Id, set.SetName, set.SetDescription, set.SetPrice,
            _mediaUrls.BuildUrl(set.MediaId, "card"),
            menu.SetItems.Where(item => item.SetId == set.Id).Select(ClientContentMappings.MapMenuSetItem).ToArray())).ToArray();
        return new MenuDto((await pricingTask).Select(ClientContentMappings.MapPricingRule).ToArray(), categories, sets, showSets);
    }

    private static bool ResolveShowSets(IReadOnlyList<SiteSettingRow> settings)
    {
        var raw = settings.FirstOrDefault()?.SettingValue;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        try
        {
            using var document = JsonDocument.Parse(raw);
            return !document.RootElement.TryGetProperty("showSets", out var value)
                || value.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
