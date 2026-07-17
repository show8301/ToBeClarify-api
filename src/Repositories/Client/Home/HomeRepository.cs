using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Home;

public sealed class HomeRepository : DapperRepositoryBase, IHomeRepository
{
    public HomeRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<HomeEventCarouselRow>> GetHomeEventCarouselsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT C.`ID` AS Id, C.`EVENT_ID` AS EventId, C.`TITLE_SNAPSHOT` AS TitleSnapshot,
                   C.`SUMMARY_SNAPSHOT` AS SummarySnapshot, C.`EVENT_TIME_SNAPSHOT` AS EventTimeSnapshot,
                   C.`CTA_LABEL` AS CtaLabel, CASE WHEN E.`ID` IS NULL THEN FALSE ELSE TRUE END AS EventExists
            FROM `HOME_EVENT_CAROUSELS` C
            LEFT JOIN `EVENTS` E ON E.`ID` = C.`EVENT_ID` AND E.`IS_PUBLISHED` = TRUE
            WHERE C.`IS_ENABLED` = TRUE
            ORDER BY C.`SORT_ORDER`, C.`CREATED_AT` DESC;
            """;
        return await QueryAsync<HomeEventCarouselRow>(sql, null, cancellationToken);
    }
}
