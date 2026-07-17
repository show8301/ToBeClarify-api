using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Events;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/events")]
public sealed class EventsController : ControllerBase
{
    private readonly IEventService _service;
    public EventsController(IEventService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EventDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EventDto>>>> GetAll(
        [FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<EventDto>>.Ok(await _service.GetEventsAsync(status, from, to, limit, cancellationToken)));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetOne(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<EventDto>.Ok(await _service.GetEventAsync(id, cancellationToken)));
}
