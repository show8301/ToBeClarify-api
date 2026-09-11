using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Rooms;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public sealed class RoomsController : ControllerBase
{
    private readonly IAdminRoomService _service;

    public RoomsController(IAdminRoomService service) => _service = service;

    [HttpGet("rooms")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminRoomDto>>>> GetRooms(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminRoomDto>>.Ok(await _service.GetRoomsAsync(cancellationToken)));

    [HttpPost("rooms")]
    public async Task<ActionResult<ApiResponse<AdminRoomDto>>> CreateRoom(
        SaveRoomRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminRoomDto>.Ok(await _service.SaveRoomAsync(null, request, User, cancellationToken)));

    [HttpPut("rooms/{id}")]
    public async Task<ActionResult<ApiResponse<AdminRoomDto>>> UpdateRoom(
        string id, SaveRoomRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AdminRoomDto>.Ok(await _service.SaveRoomAsync(id, request, User, cancellationToken)));

    [HttpDelete("rooms/{id}")]
    [Authorize(Policy = "AdminManager")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteRoom(string id, CancellationToken cancellationToken)
    {
        await _service.DeleteRoomAsync(id, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("payroll/room-profit-sharing")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<RoomProfitSharingSettingsDto>>> GetProfitSharing(
        CancellationToken cancellationToken)
        => Ok(ApiResponse<RoomProfitSharingSettingsDto>.Ok(
            await _service.GetProfitSharingSettingsAsync(cancellationToken)));

    [HttpPut("payroll/room-profit-sharing")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<RoomProfitSharingSettingsDto>>> SaveProfitSharing(
        SaveRoomProfitSharingSettingsRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<RoomProfitSharingSettingsDto>.Ok(
            await _service.SaveProfitSharingSettingsAsync(request, User, cancellationToken)));

    [HttpGet("room-orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoomServiceOrderDto>>>> GetRoomOrders(
        [FromQuery] DateOnly? businessDate, [FromQuery] string? status, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<RoomServiceOrderDto>>.Ok(
            await _service.GetRoomServiceOrdersAsync(businessDate, status, cancellationToken)));

    [HttpPost("room-orders")]
    public async Task<ActionResult<ApiResponse<RoomServiceOrderDto>>> CreateRoomOrder(
        CreateRoomServiceOrderRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<RoomServiceOrderDto>.Ok(
            await _service.CreateRoomServiceOrderAsync(request, User, cancellationToken)));

    [HttpPut("room-orders/{id}/status")]
    public async Task<ActionResult<ApiResponse<RoomServiceOrderDto>>> UpdateRoomOrderStatus(
        string id, UpdateRoomServiceOrderStatusRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<RoomServiceOrderDto>.Ok(
            await _service.UpdateRoomServiceOrderStatusAsync(id, request, User, cancellationToken)));
}
