using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/admin")]
public sealed class AdmissionContractsController(AdmissionContractService service, AdmissionCreationService creation) : ControllerBase
{
    [HttpPost("order-sessions/with-admission")]
    public async Task<ActionResult<ApiResponse<AdmissionCreationIssuedDto>>> Create(CreateAdmissionSessionRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdmissionCreationIssuedDto>.Ok(await creation.CreateAsync(request, User, ct)));

    [HttpGet("order-session-admission-policy")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdmissionPolicyDto>>> Policy(CancellationToken ct)
        => Ok(ApiResponse<AdmissionPolicyDto>.Ok(await service.GetPolicyAsync(ct)));

    [HttpPut("order-session-admission-policy")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<AdmissionPolicyDto>>> SavePolicy(SaveAdmissionPolicyRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdmissionPolicyDto>.Ok(await service.SavePolicyAsync(request, User, ct)));

    [HttpGet("order-sessions/admission-preview")]
    public async Task<ActionResult<ApiResponse<AdmissionPreviewDto>>> Preview(CancellationToken ct)
        => Ok(ApiResponse<AdmissionPreviewDto>.Ok(await service.PreviewAsync(User, ct)));

    [HttpGet("order-sessions/creation-operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<AdmissionCreationResultDto>>> Operation(Guid operationId, CancellationToken ct)
        => Ok(ApiResponse<AdmissionCreationResultDto>.Ok(await service.GetOperationAsync(operationId, User, ct)));
}
