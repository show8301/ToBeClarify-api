using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Customers;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController, Authorize(Policy = "AdminOnly"), Route("api/admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerDeliveryController(CustomerIdentityService customers, ArtDeliveryService deliveries) : ControllerBase
{
    [HttpGet("customer-history")]
    public async Task<IActionResult> History([FromQuery] DateOnly? businessDate, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
        => Ok(ApiResponse<CustomerHistoryPage>.Ok(await customers.History(businessDate, search, page, pageSize, ct)));

    [HttpGet("customers/{uid}")]
    public async Task<IActionResult> Customer(string uid, CancellationToken ct)
        => Ok(ApiResponse<CustomerDetailDto>.Ok(await customers.Detail(uid, ct)));

    [HttpGet("customer-identity/candidates")]
    public async Task<IActionResult> Candidates([FromQuery] string gameId, CancellationToken ct)
        => Ok(ApiResponse<CustomerIdentityCandidatesDto>.Ok(await customers.Candidates(gameId, ct)));

    [HttpPost("order-sessions/{sessionId}/customer-profile")]
    public async Task<IActionResult> Issue(string sessionId, LinkCustomerProfileRequest request, CancellationToken ct)
        => Ok(ApiResponse<CustomerProfileDto>.Ok(await customers.LinkFromAdmin(sessionId, request, User, ct)));

    [HttpGet("art-deliveries")]
    public async Task<IActionResult> List([FromQuery] string? sessionId, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<ArtDeliveryDto>>.Ok(await deliveries.List(sessionId, status, search, page, pageSize, ct)));

    [HttpGet("art-deliveries/{id}")]
    public async Task<IActionResult> Delivery(string id, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryDto>.Ok(await deliveries.Get(id, ct)));

    [HttpPost("art-deliveries")]
    public async Task<IActionResult> Create(CreateArtDeliveryRequest request, CancellationToken ct)
        => StatusCode(201, ApiResponse<ArtDeliveryIssuedDto>.Ok(await deliveries.Create(request, User, ct)));

    [HttpPut("art-deliveries/{id}")]
    public async Task<IActionResult> Update(string id, UpdateArtDeliveryRequest request, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryDto>.Ok(await deliveries.Update(id, request, User, ct)));

    [HttpPost("art-deliveries/{id}/reissue-code"), Authorize(Policy = "AdminManager")]
    public async Task<IActionResult> ReissueCode(string id, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryIssuedDto>.Ok(await deliveries.Reissue(id, User, ct)));

    [HttpPost("art-deliveries/{id}/assets/link")]
    public async Task<IActionResult> Link(string id, AddDeliveryLinkRequest request, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryDto>.Ok(await deliveries.AddLink(id, request, User, ct)));

    [HttpPost("art-deliveries/{id}/assets/upload"), Consumes("multipart/form-data"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string id, IFormFile file, [FromForm] string? label, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryDto>.Ok(await deliveries.Upload(id, file, label, User, ct)));

    [HttpDelete("art-deliveries/{id}/assets/{assetId}")]
    public async Task<IActionResult> RemoveAsset(string id, string assetId, CancellationToken ct)
        => Ok(ApiResponse<ArtDeliveryDto>.Ok(await deliveries.RemoveAsset(id, assetId, User, ct)));

    [HttpGet("art-deliveries/{id}/assets/{assetId}")]
    public async Task<IActionResult> Image(string id, string assetId, CancellationToken ct)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(await deliveries.AdminImage(id, assetId, ct), "image/png");
    }
}
