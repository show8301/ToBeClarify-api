using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/media")]
public sealed class AdminMediaController : ControllerBase
{
    private readonly AdminMediaUploadService _service;

    public AdminMediaController(AdminMediaUploadService service) => _service = service;

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<AdminMediaUploadDto>>> Upload(
        IFormFile file,
        [FromForm] string? category,
        CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminMediaUploadDto>.Ok(await _service.UploadAsync(file, category, User, cancellationToken)));
}
