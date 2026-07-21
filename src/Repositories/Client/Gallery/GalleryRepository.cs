using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Gallery;

public sealed class GalleryRepository : DapperRepositoryBase, IGalleryRepository
{
    public GalleryRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<GalleryAlbumRow>> GetGalleryAlbumsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `ALBUM_TITLE` AS AlbumTitle, `ALBUM_DESCRIPTION` AS AlbumDescription,
                   `COVER_MEDIA_ID` AS CoverMediaId, `COVER_IMAGE_URL` AS LegacyCoverImageUrl,
                   `PERIOD_TEXT` AS PeriodText,
                   `ENDS_AT` AS EndsAt, `DETAIL_CONTENT` AS DetailContent
            FROM `GALLERY_ALBUMS` WHERE `IS_PUBLISHED` = TRUE ORDER BY `SORT_ORDER`, `ALBUM_TITLE`;
            """;
        return await QueryAsync<GalleryAlbumRow>(sql, null, cancellationToken);
    }

    public async Task<GalleryAlbumRow?> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `ALBUM_TITLE` AS AlbumTitle, `ALBUM_DESCRIPTION` AS AlbumDescription,
                   `COVER_MEDIA_ID` AS CoverMediaId, `COVER_IMAGE_URL` AS LegacyCoverImageUrl,
                   `PERIOD_TEXT` AS PeriodText,
                   `ENDS_AT` AS EndsAt, `DETAIL_CONTENT` AS DetailContent
            FROM `GALLERY_ALBUMS` WHERE `ID` = @Id AND `IS_PUBLISHED` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<GalleryAlbumRow>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IReadOnlyList<GalleryItemRow>> GetGalleryItemsAsync(string albumId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `ALBUM_ID` AS AlbumId, `MEDIA_ID` AS MediaId,
                   `IMAGE_URL` AS LegacyImageUrl, `THUMBNAIL_URL` AS LegacyThumbnailUrl,
                   `TITLE` AS Title, `CAPTION` AS Caption, `SHOT_AT` AS ShotAt
            FROM `GALLERY_ITEMS`
            WHERE `ALBUM_ID` = @AlbumId AND `IS_PUBLISHED` = TRUE
            ORDER BY `SORT_ORDER`, `SHOT_AT` DESC, `CREATED_AT` DESC;
            """;
        return await QueryAsync<GalleryItemRow>(sql, new { AlbumId = albumId }, cancellationToken);
    }
}
