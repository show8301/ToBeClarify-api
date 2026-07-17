using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Rankings;

public interface IRankingRepository
{
    Task<IReadOnlyList<RankingRow>> GetRankingsAsync(string rankingType, string? periodLabel, CancellationToken cancellationToken);
}
