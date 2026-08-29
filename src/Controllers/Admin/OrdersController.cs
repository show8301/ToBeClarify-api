using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderingService _service;

    public OrdersController(IOrderingService service) => _service = service;

    [HttpGet("order-sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminOrderSessionDto>>>> Sessions(
        [FromQuery] DateOnly? businessDate, [FromQuery] string? search, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AdminOrderSessionDto>>.Ok(
            await _service.GetAdminSessionsAsync(businessDate, search, cancellationToken)));

    [HttpPost("order-sessions")]
    public async Task<ActionResult<ApiResponse<OrderSessionIssuedDto>>> CreateSession(
        CreateOrderSessionRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderSessionIssuedDto>.Ok(
            await _service.CreateSessionAsync(request, User, cancellationToken)));

    [HttpPut("order-sessions/{sessionId}")]
    public async Task<ActionResult<ApiResponse<OrderSessionDto>>> UpdateSession(
        string sessionId, UpdateOrderSessionRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderSessionDto>.Ok(
            await _service.UpdateSessionAsync(sessionId, request, User, cancellationToken)));

    [HttpPost("order-sessions/{sessionId}/reissue")]
    public async Task<ActionResult<ApiResponse<OrderSessionIssuedDto>>> ReissueSession(
        string sessionId, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderSessionIssuedDto>.Ok(
            await _service.RotateSessionCredentialsAsync(sessionId, User, cancellationToken)));

    [HttpGet("order-sessions/{sessionId}/orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderDto>>>> SessionOrders(
        string sessionId, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<OrderDto>>.Ok(
            await _service.GetAdminOrdersAsync(sessionId, cancellationToken)));

    [HttpGet("ordering-settings")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<OrderingSettingsDto>>> Settings(CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderingSettingsDto>.Ok(await _service.GetSettingsAsync(cancellationToken)));

    [HttpPut("ordering-settings")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<OrderingSettingsDto>>> SaveSettings(
        UpdateOrderingSettingsRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderingSettingsDto>.Ok(
            await _service.SaveSettingsAsync(request, User, cancellationToken)));

    [HttpPost("ordering-settings/pause-nomination")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<OrderingSettingsDto>>> PauseNomination(
        PauseNominationRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderingSettingsDto>.Ok(
            await _service.PauseNominationAsync(request, User, cancellationToken)));

    [HttpPost("orders/{orderId}/confirm-nominee")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ConfirmNominee(
        string orderId, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(await _service.ConfirmNomineeAsync(orderId, User, cancellationToken)));

    [HttpPost("orders/{orderId}/reschedule")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Reschedule(
        string orderId, RescheduleOrderRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(
            await _service.RescheduleOrderAsync(orderId, request, User, cancellationToken)));

    [HttpPut("orders/{orderId}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrder(
        string orderId, UpdateAdminOrderRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(await _service.UpdateOrderAsync(orderId, request, User, cancellationToken)));

    [HttpPut("orders/{orderId}/items/{itemId}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateItem(
        string orderId, string itemId, UpdateOrderItemRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(
            await _service.UpdateOrderItemAsync(orderId, itemId, request, User, cancellationToken)));

    [HttpDelete("orders/{orderId}/items/{itemId}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> DeleteItem(
        string orderId, string itemId, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(
            await _service.DeleteOrderItemAsync(orderId, itemId, User, cancellationToken)));

    [HttpDelete("orders/{orderId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelOrder(
        string orderId, [FromBody] OrderActionRequest request, CancellationToken cancellationToken)
    {
        await _service.CancelOrderAsync(orderId, request, User, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("ordering-reports")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderingReportSummaryDto>>>> Report(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<OrderingReportSummaryDto>>.Ok(
            await _service.GetReportAsync(from, to, cancellationToken)));
}
