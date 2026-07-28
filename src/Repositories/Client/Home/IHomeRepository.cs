using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Home;

public interface IHomeRepository
{
    Task<IReadOnlyList<HomeEventCarouselRow>> GetHomeEventCarouselsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HomeSlideRow>> GetHomeSlidesAsync(CancellationToken cancellationToken);
}
