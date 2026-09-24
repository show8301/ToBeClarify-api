using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Guestbook;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController, Authorize(Policy = "AdminOnly"), Route("api/admin/guestbook")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536)]
public sealed class AdminGuestbookController(AdminGuestbookService service) : ControllerBase
{
    [HttpGet("threads")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string filter = "all",
        [FromQuery] string? search = null,
        [FromQuery] string hasImage = "all",
        CancellationToken ct = default)
        => Ok(ApiResponse<GuestbookList>.Ok(await service.List(page, pageSize, filter, search, hasImage, ct)));
    [HttpGet("threads/{id}/replies")]
    public async Task<IActionResult> Replies(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null, CancellationToken ct = default)
        => Ok(ApiResponse<GuestbookReplies>.Ok(await service.Replies(id, page, pageSize, ct, cursor)));
    [HttpPost("threads"), RequestSizeLimit(3145728)]
    public async Task<IActionResult> Create(GuestbookWrite write, CancellationToken ct) => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(null, write, User, ct)));
    [HttpPost("threads/{id}/replies"), RequestSizeLimit(3145728)]
    public async Task<IActionResult> Reply(string id, GuestbookWrite write, CancellationToken ct) => StatusCode(201, ApiResponse<GuestbookMessage>.Ok(await service.Create(id, write, User, ct)));
    [HttpPut("threads/{id}"), HttpPut("threads/{id}/replies/{replyId}")]
    public async Task<IActionResult> Edit(string id, GuestbookEdit edit, CancellationToken ct, string? replyId = null) => Ok(ApiResponse<GuestbookMessage>.Ok(await service.Edit(id, replyId, edit, User, ct)));
    [HttpDelete("threads/{id}/image"), HttpDelete("threads/{id}/replies/{replyId}/image")]
    public async Task<IActionResult> RemoveImage(string id, [FromQuery, Range(1, int.MaxValue)] int version, CancellationToken ct, string? replyId = null)
        => Ok(ApiResponse<GuestbookMessage>.Ok(await service.RemoveImage(id, replyId, version, User, ct)));
    [HttpPatch("threads/{id}"), HttpPatch("threads/{id}/replies/{replyId}")]
    public async Task<IActionResult> Moderate(string id, GuestbookModerate change, CancellationToken ct, string? replyId = null) => Ok(ApiResponse<GuestbookMessage>.Ok(await service.Moderate(id, replyId, change, User, ct)));
    [HttpPut("pins")]
    public async Task<IActionResult> Reorder(GuestbookPinOrder order, CancellationToken ct) { await service.Reorder(order, User, ct); return Ok(ApiResponse<bool>.Ok(true)); }
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(ApiResponse<GuestbookSettings>.Ok(await service.Settings(ct)));
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(GuestbookSettings settings, CancellationToken ct) { await service.SaveSettings(settings, User, ct); return Ok(ApiResponse<bool>.Ok(true)); }
    [HttpGet("threads/{id}/history")]
    public async Task<IActionResult> History(string id, CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<object>>.Ok(await service.History(id, ct)));
}
