using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Rankings;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Rankings;

public sealed class RankingService : IRankingService
{
    private readonly IRankingRepository _repository;

    public RankingService(IRankingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<RankingDto>> GetRankingsAsync(string rankingType, string? periodLabel, CancellationToken cancellationToken)
    {
        if (!ClientContentMappings.RankingTypes.Contains(rankingType))
            throw new BusinessException("Ranking type must be staffRanking or monetaryRanking.", "INVALID_RANKING_TYPE");
        if (periodLabel is { Length: > 40 }) throw new BusinessException("Period label is too long.", "INVALID_PERIOD_LABEL");
        var rows = await _repository.GetRankingsAsync(rankingType,
            string.IsNullOrWhiteSpace(periodLabel) ? null : periodLabel.Trim(), cancellationToken);
        return rows.Select(row => new RankingDto(row.Id, row.RankingType, row.TargetId, row.DisplayNameSnapshot,
            row.AvatarSnapshot, row.TitleBadge, row.RankPosition, row.ScoreValue, row.ScoreLabel, row.PeriodLabel)).ToArray();
    }
}
