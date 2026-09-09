using System.Text.Json;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Client.Menu;
using ToBeClarify.Api.Repositories.Client.Site;
using ToBeClarify.Api.Services.Client.Shared;
using ToBeClarify.Api.Services.Media;
using ToBeClarify.Api.Services.Menu;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Services.Client.Menu;

public sealed class MenuService : IMenuService
{
    private readonly IMenuRepository _repository;
    private readonly ISiteRepository _siteRepository;
    private readonly MediaUrlService _mediaUrls;
    private readonly IOrderingRepository _ordering;
    private readonly IAppClock _clock;

    public MenuService(IMenuRepository repository, ISiteRepository siteRepository, MediaUrlService mediaUrls, IOrderingRepository ordering, IAppClock clock)
    {
        _repository = repository;
        _siteRepository = siteRepository;
        _mediaUrls = mediaUrls;
        _ordering = ordering;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PricingRuleDto>> GetPricingRulesAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetPricingRulesAsync(cancellationToken);
        var settings = await _ordering.GetSettingsAsync(cancellationToken);
        var now = new DateTimeOffset(_clock.LocalDateTime, TimeSpan.FromHours(8));
        return rows.Select(ClientContentMappings.MapPricingRule)
            .Where(x => (!x.Policy.ValidFrom.HasValue || x.Policy.ValidFrom <= now) && (!x.Policy.ValidUntil.HasValue || x.Policy.ValidUntil > now))
            .Select(x => x.Policy.Mode != "system" ? x : x with { PriceText = x.Policy.Source switch {
                "minimum_meal_credit" => $"{settings.MinimumMealCredit:N0} Gil／每次入場餐點信物",
                "base_nomination_fee" => $"{settings.BaseNominationFee:N0} Gil／每節 {settings.SegmentMinutes} 分鐘",
                _ => null
            }}).ToArray();
    }

    public async Task<MenuDto> GetMenuAsync(CancellationToken cancellationToken, bool forOrdering = false)
    {
        var menuTask = _repository.GetMenuAsync(cancellationToken);
        var pricingTask = GetPricingRulesAsync(cancellationToken);
        var settingsTask = _siteRepository.GetSiteSettingsAsync("menuSettings", cancellationToken);
        await Task.WhenAll(menuTask, pricingTask, settingsTask);
        var menu = await menuTask;
        var showSets = ResolveShowSets(await settingsTask);
        var itemDtos = menu.Items.Select(item => ClientContentMappings.MapMenuItem(item, _mediaUrls)).Where(x => x.Policy.CanOrderAlone).ToArray();
        var categories = menu.Categories.Select(category => new MenuCategoryDto(category.Id, category.CategoryName,
            category.CategoryDescription, itemDtos.Where(item => item.CategoryId == category.Id).ToArray())).ToArray();
        var sets = (showSets || forOrdering ? menu.Sets : Array.Empty<MenuSetRow>()).Select(set => {
            var parts = menu.SetItems.Where(item => item.SetId == set.Id).Select(ClientContentMappings.MapMenuSetItem).ToArray();
            var policy = MenuPolicies.Read<ProductPolicy>(set.PolicyJson);
            return new MenuSetDto(set.Id, set.SetName, set.SetDescription, set.SetPrice, _mediaUrls.BuildUrl(set.MediaId, "card"), parts) {
                Policy = policy,
                IsOrderable = parts.Length > 0 && parts.All(x => x.IsAvailable),
                UnavailableReason = parts.Any(x => !x.IsAvailable) ? "套餐部分內容暫停供應" : null,
                EffectiveEventCategories = policy.EventCategories.Concat(parts.SelectMany(x => x.EventCategories)).Distinct().Order().ToArray()
            };
        }).Where(set => set.Items.Any(x => x.IsAvailable)).ToArray();
        return new MenuDto(await pricingTask, categories, sets, showSets);
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
