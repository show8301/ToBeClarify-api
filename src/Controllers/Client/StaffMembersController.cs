using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Staff;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/staff-members")]
public sealed class StaffMembersController : ControllerBase
{
    private readonly IStaffService _service;
    public StaffMembersController(IStaffService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StaffListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffListItemDto>>>> GetAll(
        [FromQuery] int? limit, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<StaffListItemDto>>.Ok(await _service.GetStaffAsync(limit, cancellationToken)));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<StaffDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StaffDetailDto>>> GetOne(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<StaffDetailDto>.Ok(await _service.GetStaffDetailAsync(id, cancellationToken)));

    [HttpGet("{id}/services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StaffServiceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffServiceDto>>>> GetServices(string id, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<StaffServiceDto>>.Ok(await _service.GetStaffServicesAsync(id, cancellationToken)));
}
