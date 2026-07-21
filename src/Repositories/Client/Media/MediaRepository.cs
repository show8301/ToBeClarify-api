using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Media;

public sealed class MediaRepository : DapperRepositoryBase, IMediaRepository
{
    public MediaRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public Task<MediaAssetRow?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `CATEGORY` AS Category, `STORAGE_PATH` AS StoragePath,
                   `MIME_TYPE` AS MimeType, `ORIGINAL_FILE_NAME` AS OriginalFileName,
                   `FILE_SIZE` AS FileSize, `WIDTH` AS Width, `HEIGHT` AS Height, `VERSION` AS Version
            FROM `MEDIA_ASSETS`
            WHERE `ID` = @Id AND `IS_ACTIVE` = TRUE
            LIMIT 1;
            """;
        return QuerySingleOrDefaultAsync<MediaAssetRow>(sql, new { Id = id }, cancellationToken);
    }
}
