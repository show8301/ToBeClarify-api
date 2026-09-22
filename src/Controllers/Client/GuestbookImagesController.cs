using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestbookImagesController(GuestbookStore store) : ControllerBase
{
    [HttpGet("api/client/guestbook/images/{id:guid}")]
    public async Task<IActionResult> PublicImage(string id, CancellationToken ct)
        => Image(await store.GetImage(id, false, ct));

    [Authorize(Policy = "AdminOnly"), HttpGet("api/admin/guestbook/images/{id:guid}")]
    public async Task<IActionResult> AdminImage(string id, CancellationToken ct)
        => Image(await store.GetImage(id, true, ct));

    private FileContentResult Image(byte[] bytes)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(bytes, "image/webp");
    }
}
