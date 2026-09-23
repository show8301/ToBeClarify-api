using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [HttpGet("{id}/replies/{replyId}")]
    public async Task<IActionResult> GetReply(string id, string replyId, CancellationToken ct)
        => Ok(ApiResponse<GuestbookMessage>.Ok(await service.GetReply(id, replyId, ct)));
    [HttpPost("/api/client/guestbook/items/{id}/like"), RequestSizeLimit(4096), EnableRateLimiting("customer-access")]
    public async Task<IActionResult> Like(string id, GuestbookLikeRequest request, CancellationToken ct)
        => Ok(ApiResponse<GuestbookLikeResult>.Ok(await service.Like(id, request.Liked, ct)));
    [HttpPost, RequestSizeLimit(3145728), EnableRateLimiting("customer-access")]
    public async Task<IActionResult> Create(GuestbookWrite write, CancellationToken ct)
        => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(null, write, ct)));
    [HttpPost("{id}/replies"), RequestSizeLimit(3145728), EnableRateLimiting("customer-access")]
    public async Task<IActionResult> Reply(string id, GuestbookWrite write, CancellationToken ct)
        => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(id, write, ct)));
}
