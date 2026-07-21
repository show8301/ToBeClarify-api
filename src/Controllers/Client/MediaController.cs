using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[AllowAnonymous]
[Route("api/client/media")]
public sealed class MediaController : ControllerBase
{
    private readonly MediaFileService _service;

    public MediaController(MediaFileService service) => _service = service;

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, [FromQuery] string? variant, CancellationToken cancellationToken)
    {
        var result = await _service.OpenAsync(id, variant, cancellationToken);
        Response.Headers.CacheControl = "public,max-age=86400";
        Response.Headers.ETag = $"\"{result.Version}-{result.FileName}\"";
        return File(result.Stream, result.ContentType, enableRangeProcessing: true);
    }
}
