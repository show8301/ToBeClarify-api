using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Events;

public sealed class EventRepository : DapperRepositoryBase, IEventRepository
{
    public EventRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<EventRow>> GetEventsAsync(string? status, DateTime? from, DateTime? to, int? limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `TITLE` AS Title, `SUMMARY` AS Summary,
                   `COVER_MEDIA_ID` AS CoverMediaId, `COVER_IMAGE_URL` AS LegacyCoverImageUrl,
                   `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt, `STATUS` AS Status,
                   `LOCATION_TEXT` AS LocationText, `DETAIL_CONTENT` AS DetailContent, `NOTICE_CONTENT` AS NoticeContent
            FROM `EVENTS`
            WHERE `IS_PUBLISHED` = TRUE
              AND (@Status IS NULL OR `STATUS` = @Status)
              AND (@From IS NULL OR `ENDS_AT` >= @From)
              AND (@To IS NULL OR `STARTS_AT` < @To)
            ORDER BY `SORT_ORDER`, `STARTS_AT` DESC
            LIMIT @Limit;
            """;
        return await QueryAsync<EventRow>(sql, new { Status = status, From = from, To = to, Limit = limit ?? int.MaxValue }, cancellationToken);
    }

    public async Task<EventRow?> GetEventAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `TITLE` AS Title, `SUMMARY` AS Summary,
                   `COVER_MEDIA_ID` AS CoverMediaId, `COVER_IMAGE_URL` AS LegacyCoverImageUrl,
                   `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt, `STATUS` AS Status,
                   `LOCATION_TEXT` AS LocationText, `DETAIL_CONTENT` AS DetailContent, `NOTICE_CONTENT` AS NoticeContent
            FROM `EVENTS` WHERE `ID` = @Id AND `IS_PUBLISHED` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<EventRow>(sql, new { Id = id }, cancellationToken);
    }
}
