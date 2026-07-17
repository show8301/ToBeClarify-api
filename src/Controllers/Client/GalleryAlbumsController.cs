using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Gallery;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/gallery-albums")]
public sealed class GalleryAlbumsController : ControllerBase
{
    private readonly IGalleryService _service;
    public GalleryAlbumsController(IGalleryService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<GalleryAlbumDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GalleryAlbumDto>>>> GetAll(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<GalleryAlbumDto>>.Ok(await _service.GetGalleryAlbumsAsync(cancellationToken)));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<GalleryAlbumDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GalleryAlbumDetailDto>>> GetOne(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<GalleryAlbumDetailDto>.Ok(await _service.GetGalleryAlbumAsync(id, cancellationToken)));

    [HttpGet("{id}/items")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<GalleryItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GalleryItemDto>>>> GetItems(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<GalleryItemDto>>.Ok(await _service.GetGalleryItemsAsync(id, cancellationToken)));
}
