using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Home;
using ToBeClarify.Api.Services.Client.Shared;
using ToBeClarify.Api.Services.Client.Site;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Client.Home;

public sealed class HomeService : IHomeService
{
    private readonly IHomeRepository _repository;
    private readonly ISiteService _siteService;
    private readonly MediaUrlService _mediaUrls;

    public HomeService(
        IHomeRepository repository,
        ISiteService siteService,
        MediaUrlService mediaUrls)
    {
        _repository = repository;
        _siteService = siteService;
        _mediaUrls = mediaUrls;
    }

    public async Task<IReadOnlyList<HomeEventCarouselDto>> GetCarouselsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetHomeEventCarouselsAsync(cancellationToken);
        return rows.Select(row => new HomeEventCarouselDto(row.Id, row.AlbumId, row.Title,
            row.Summary, row.EventTimeSnapshot, row.CtaLabel,
            _mediaUrls.BuildUrl(row.MediaId, "hero"), row.AlbumExists)).ToArray();
    }

    public async Task<IReadOnlyList<HomeSlideDto>> GetSlidesAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetHomeSlidesAsync(cancellationToken);
        return rows.Select(row => new HomeSlideDto(row.Id,
            _mediaUrls.BuildUrl(row.MediaId, "hero"), row.DisplaySeconds)).ToArray();
    }

    public async Task<HomeDto> GetHomeAsync(CancellationToken cancellationToken)
    {
        var settingsTask = _siteService.GetSiteSettingsAsync(cancellationToken);
        var navigationTask = _siteService.GetNavigationAsync("navbar", cancellationToken);
        var carouselsTask = GetCarouselsAsync(cancellationToken);
        var slidesTask = GetSlidesAsync(cancellationToken);
        var rulesTask = _siteService.GetShopRulesAsync(cancellationToken);
        await Task.WhenAll(settingsTask, navigationTask, carouselsTask, slidesTask, rulesTask);
        return new HomeDto(await settingsTask, await navigationTask, await carouselsTask, await slidesTask,
            await rulesTask);
    }
}
