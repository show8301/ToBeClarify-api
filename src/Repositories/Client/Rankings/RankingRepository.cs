using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Rankings;

public sealed class RankingRepository : DapperRepositoryBase, IRankingRepository
{
    public RankingRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<RankingRow>> GetRankingsAsync(string rankingType, string? periodLabel, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `RANKING_TYPE` AS RankingType, `TARGET_ID` AS TargetId,
                   `DISPLAY_NAME_SNAPSHOT` AS DisplayNameSnapshot, `AVATAR_SNAPSHOT` AS AvatarSnapshot,
                   `TITLE_BADGE` AS TitleBadge, `RANK_POSITION` AS RankPosition, `SCORE_VALUE` AS ScoreValue,
                   `SCORE_LABEL` AS ScoreLabel, `PERIOD_LABEL` AS PeriodLabel
            FROM `RANKINGS`
            WHERE `RANKING_TYPE` = @RankingType AND `IS_PUBLISHED` = TRUE
              AND `PERIOD_LABEL` <=> COALESCE(
                    @PeriodLabel,
                    (SELECT MAX(R2.`PERIOD_LABEL`) FROM `RANKINGS` R2
                     WHERE R2.`RANKING_TYPE` = @RankingType AND R2.`IS_PUBLISHED` = TRUE))
            ORDER BY `RANK_POSITION`;
            """;
        return await QueryAsync<RankingRow>(sql, new { RankingType = rankingType, PeriodLabel = periodLabel }, cancellationToken);
    }
}
