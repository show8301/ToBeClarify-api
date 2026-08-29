using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/ordering")]
public sealed class OrderingController : ControllerBase
{
    private readonly IOrderingService _service;

    public OrderingController(IOrderingService service) => _service = service;

    [HttpPost("access")]
    [EnableRateLimiting("ordering-access")]
    public async Task<ActionResult<ApiResponse<OrderSessionAccessDto>>> Access(
        AccessOrderSessionRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderSessionAccessDto>.Ok(
            await _service.AccessSessionAsync(request.OrderToken, cancellationToken)));

    [HttpPost("recover")]
    [EnableRateLimiting("ordering-recovery")]
    public async Task<ActionResult<ApiResponse<OrderSessionIssuedDto>>> Recover(
        RecoverOrderSessionRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderSessionIssuedDto>.Ok(
            await _service.RecoverSessionAsync(request, cancellationToken)));

    [HttpGet("catalog")]
    [EnableRateLimiting("ordering-access")]
    public async Task<ActionResult<ApiResponse<OrderCatalogDto>>> Catalog(CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderCatalogDto>.Ok(await _service.GetCatalogAsync(Token(), cancellationToken)));

    [HttpGet("orders")]
    [EnableRateLimiting("ordering-access")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderDto>>>> Orders(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<OrderDto>>.Ok(await _service.GetMyOrdersAsync(Token(), cancellationToken)));

    [HttpPost("orders")]
    [EnableRateLimiting("ordering-write")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Submit(
        SubmitOrderRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<OrderDto>.Ok(await _service.SubmitOrderAsync(Token(), request, cancellationToken)));

    private string Token()
        => Request.Headers.TryGetValue("X-Order-Token", out var token) && !string.IsNullOrWhiteSpace(token)
            ? token.ToString()
            : throw new BusinessException("缺少點餐碼。", "ORDER_TOKEN_REQUIRED");
}
