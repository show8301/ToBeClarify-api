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
            SELECT C.`ID` AS Id, C.`ALBUM_ID` AS AlbumId,
                   COALESCE(C.`OVERRIDE_TITLE`, A.`ALBUM_TITLE`) AS Title,
                   COALESCE(C.`OVERRIDE_SUMMARY`, A.`ALBUM_DESCRIPTION`) AS Summary,
                   COALESCE(C.`OVERRIDE_MEDIA_ID`, A.`COVER_MEDIA_ID`) AS MediaId,
                   C.`EVENT_TIME_SNAPSHOT` AS EventTimeSnapshot,
                   C.`CTA_LABEL` AS CtaLabel, CASE WHEN A.`ID` IS NULL THEN FALSE ELSE TRUE END AS AlbumExists
            FROM `HOME_EVENT_CAROUSELS` C
            LEFT JOIN `GALLERY_ALBUMS` A ON A.`ID` = C.`ALBUM_ID` AND A.`IS_PUBLISHED` = TRUE
            WHERE C.`IS_ENABLED` = TRUE
            ORDER BY C.`SORT_ORDER`, C.`CREATED_AT` DESC;
            """;
        return await QueryAsync<HomeEventCarouselRow>(sql, null, cancellationToken);
    }

    public Task<IReadOnlyList<HomeSlideRow>> GetHomeSlidesAsync(CancellationToken cancellationToken)
        => QueryAsync<HomeSlideRow>("""
            SELECT `ID` AS Id, `MEDIA_ID` AS MediaId
            FROM `HOME_SLIDES`
            WHERE `IS_ENABLED` = TRUE
            ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, null, cancellationToken);
}
