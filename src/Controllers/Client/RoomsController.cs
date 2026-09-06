using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Rooms;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly IRoomService _service;

    public RoomsController(IRoomService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<RoomListDto>>> GetRooms(CancellationToken cancellationToken)
        => Ok(ApiResponse<RoomListDto>.Ok(await _service.GetRoomsAsync(cancellationToken)));

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoomStatusDto>>>> GetStatus(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<RoomStatusDto>>.Ok(
            await _service.GetRoomStatusesAsync(from, to, cancellationToken)));
}
