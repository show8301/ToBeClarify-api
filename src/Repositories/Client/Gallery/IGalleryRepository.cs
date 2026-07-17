using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Gallery;

public interface IGalleryRepository
{
    Task<IReadOnlyList<GalleryAlbumRow>> GetGalleryAlbumsAsync(CancellationToken cancellationToken);
    Task<GalleryAlbumRow?> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GalleryItemRow>> GetGalleryItemsAsync(string albumId, CancellationToken cancellationToken);
}
