using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Gallery;

public interface IGalleryService
{
    Task<IReadOnlyList<GalleryAlbumDto>> GetGalleryAlbumsAsync(CancellationToken cancellationToken);
    Task<GalleryAlbumDetailDto> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GalleryItemDto>> GetGalleryItemsAsync(string id, CancellationToken cancellationToken);
}
