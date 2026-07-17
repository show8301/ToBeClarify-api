using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Home;
using ToBeClarify.Api.Services.Client.Events;
using ToBeClarify.Api.Services.Client.Shared;
using ToBeClarify.Api.Services.Client.Site;
using ToBeClarify.Api.Services.Client.Staff;

namespace ToBeClarify.Api.Services.Client.Home;

public sealed class HomeService : IHomeService
{
    private readonly IHomeRepository _repository;
    private readonly ISiteService _siteService;
    private readonly IStaffService _staffService;
    private readonly IEventService _eventService;
    private readonly IAppClock _clock;

    public HomeService(
        IHomeRepository repository,
        ISiteService siteService,
        IStaffService staffService,
        IEventService eventService,
        IAppClock clock)
    {
        _repository = repository;
        _siteService = siteService;
        _staffService = staffService;
        _eventService = eventService;
        _clock = clock;
    }

    public async Task<IReadOnlyList<HomeEventCarouselDto>> GetCarouselsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetHomeEventCarouselsAsync(cancellationToken);
        return rows.Select(row => new HomeEventCarouselDto(row.Id, row.EventId, row.TitleSnapshot,
            row.SummarySnapshot, row.EventTimeSnapshot, row.CtaLabel, row.EventExists)).ToArray();
    }

    public async Task<HomeDto> GetHomeAsync(CancellationToken cancellationToken)
    {
        var settingsTask = _siteService.GetSiteSettingsAsync(cancellationToken);
        var navigationTask = _siteService.GetNavigationAsync("navbar", cancellationToken);
        var carouselsTask = GetCarouselsAsync(cancellationToken);
        var rulesTask = _siteService.GetShopRulesAsync(cancellationToken);
        var staffTask = _staffService.GetStaffAsync(8, cancellationToken);
        var eventsTask = _eventService.GetEventsAsync(null, _clock.Now, null, 8, cancellationToken);
        await Task.WhenAll(settingsTask, navigationTask, carouselsTask, rulesTask, staffTask, eventsTask);
        return new HomeDto(await settingsTask, await navigationTask, await carouselsTask,
            await rulesTask, await staffTask, await eventsTask);
    }
}
