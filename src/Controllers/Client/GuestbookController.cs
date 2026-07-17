using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Guestbook;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/guestbook/comments")]
public sealed class GuestbookController : ControllerBase
{
    private readonly IGuestbookService _service;
    public GuestbookController(IGuestbookService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<GuestbookPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GuestbookPageDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(ApiResponse<GuestbookPageDto>.Ok(await _service.GetGuestbookCommentsAsync(page, pageSize, cancellationToken)));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<GuestbookCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GuestbookCommentDto>>> GetOne(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<GuestbookCommentDto>.Ok(await _service.GetGuestbookCommentAsync(id, cancellationToken)));

    [HttpPost]
    [EnableRateLimiting("guestbook-write")]
    [ProducesResponseType(typeof(ApiResponse<GuestbookCommentDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<GuestbookCommentDto>>> Create(
        [FromBody] CreateGuestbookCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateGuestbookCommentAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetOne), new { id = result.Id }, ApiResponse<GuestbookCommentDto>.Ok(result));
    }

    [HttpPost("{id}/replies")]
    [EnableRateLimiting("guestbook-write")]
    [ProducesResponseType(typeof(ApiResponse<GuestbookReplyDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<GuestbookReplyDto>>> CreateReply(
        string id, [FromBody] CreateGuestbookReplyRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateGuestbookReplyAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GuestbookReplyDto>.Ok(result));
    }
}
