using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Home;

public interface IHomeService
{
    Task<IReadOnlyList<HomeEventCarouselDto>> GetCarouselsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HomeSlideDto>> GetSlidesAsync(CancellationToken cancellationToken);
    Task<HomeDto> GetHomeAsync(CancellationToken cancellationToken);
}
