using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Rankings;

public interface IRankingService
{
    Task<IReadOnlyList<RankingDto>> GetRankingsAsync(string rankingType, string? periodLabel, CancellationToken cancellationToken);
}
