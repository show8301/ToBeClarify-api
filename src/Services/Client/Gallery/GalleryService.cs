using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Gallery;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Gallery;

public sealed class GalleryService : IGalleryService
{
    private readonly IGalleryRepository _repository;

    public GalleryService(IGalleryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GalleryAlbumDto>> GetGalleryAlbumsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetGalleryAlbumsAsync(cancellationToken);
        return rows.Select(ClientContentMappings.MapGalleryAlbum).ToArray();
    }

    public async Task<GalleryAlbumDetailDto> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken)
    {
        var albumId = ClientContentMappings.RequiredId(id);
        var album = await _repository.GetGalleryAlbumAsync(albumId, cancellationToken)
            ?? throw new NotFoundException("Gallery album not found.", "GALLERY_ALBUM_NOT_FOUND");
        var items = await _repository.GetGalleryItemsAsync(albumId, cancellationToken);
        return new GalleryAlbumDetailDto(album.Id, album.AlbumTitle, album.AlbumDescription,
            album.CoverImageUrl, album.PeriodText, album.EndsAt.HasValue ? ClientContentMappings.ToTaiwanOffset(album.EndsAt.Value) : null,
            ClientContentMappings.ParseStringArray(album.DetailContent), items.Select(ClientContentMappings.MapGalleryItem).ToArray());
    }

    public async Task<IReadOnlyList<GalleryItemDto>> GetGalleryItemsAsync(string id, CancellationToken cancellationToken)
        => (await GetGalleryAlbumAsync(id, cancellationToken)).Items;
}
