using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Guestbook;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController, Route("api/client/guestbook/threads")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestbookBoardController(GuestbookBoardService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null, CancellationToken ct = default)
        => Ok(ApiResponse<GuestbookList>.Ok(await service.List(page, pageSize, ct, cursor)));
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) => Ok(ApiResponse<GuestbookMessage>.Ok(await service.Get(id, ct)));
    [HttpGet("{id}/replies")]
    public async Task<IActionResult> Replies(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null, CancellationToken ct = default)
        => Ok(ApiResponse<GuestbookReplies>.Ok(await service.Replies(id, page, pageSize, ct, cursor)));
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Create(GuestbookWrite write, CancellationToken ct)
        => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(null, write, ct)));
    [HttpPost("{id}/replies"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Reply(string id, GuestbookWrite write, CancellationToken ct)
        => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(id, write, ct)));
}
