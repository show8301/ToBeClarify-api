using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Customers;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController, Route("api/client"), EnableRateLimiting("customer-access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerDeliveryController(ArtDeliveryService deliveries) : ControllerBase
{
    [HttpPost("art-deliveries/lookup"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Lookup(DeliveryAccessRequest request, CancellationToken ct)
        => Ok(ApiResponse<PublicDeliveryListDto>.Ok(await deliveries.Lookup(request, ct)));

    [HttpPost("art-deliveries/{id}/acknowledge"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Acknowledge(string id, DeliveryAccessRequest request, CancellationToken ct)
        => Ok(ApiResponse<PublicArtDeliveryDto>.Ok(await deliveries.Acknowledge(id, request, ct)));

    [HttpGet("art-deliveries/{id}/assets/{assetId}"), EnableRateLimiting("customer-media")]
    public async Task<IActionResult> Image(string id, string assetId, CancellationToken ct)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var request = new DeliveryAccessRequest
        {
            ClaimCode = Request.Headers["X-Delivery-Code"].ToString(),
            CustomerUid = Request.Headers["X-Customer-Uid"].ToString()
        };
        return File(await deliveries.ClientImage(id, assetId, request, ct), "image/png");
    }
}
