using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Home;

public interface IHomeRepository
{
    Task<IReadOnlyList<HomeEventCarouselRow>> GetHomeEventCarouselsAsync(CancellationToken cancellationToken);
}
