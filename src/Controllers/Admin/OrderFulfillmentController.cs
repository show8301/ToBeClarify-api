using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/orders/{orderId}/fulfillment")]
public sealed class OrderFulfillmentController(IOrderingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<OrderFulfillmentDto>>> Get(string orderId, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderFulfillmentDto>.Ok(await service.GetFulfillmentAsync(orderId, User, cancellationToken)));

    [HttpGet("{unitId}/start-preview")]
    public async Task<ActionResult<ApiResponse<FulfillmentStartPreviewDto>>> StartPreview(string orderId, string unitId,
        CancellationToken cancellationToken)
        => Ok(ApiResponse<FulfillmentStartPreviewDto>.Ok(await service.GetFulfillmentStartPreviewAsync(orderId, unitId, User, cancellationToken)));

    [HttpPost("{unitId}/transition")]
    public async Task<ActionResult<ApiResponse<OrderFulfillmentDto>>> Transition(string orderId, string unitId,
        FulfillmentTransitionRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderFulfillmentDto>.Ok(await service.TransitionFulfillmentAsync(orderId, unitId, request, User, cancellationToken)));
}
