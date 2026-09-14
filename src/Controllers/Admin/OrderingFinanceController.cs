using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/ordering-finance/sessions/{sessionId}")]
public sealed class OrderingFinanceController(OrderingFinanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<OrderingFinanceAccountDto>>> Get(string sessionId, CancellationToken ct)
        => Ok(ApiResponse<OrderingFinanceAccountDto>.Ok(await service.GetAccountAsync(sessionId, ct)));

    [HttpGet("operations/{operationId}")]
    public async Task<ActionResult<ApiResponse<OrderingFinanceOperationDto?>>> Operation(
        string sessionId, string operationId, CancellationToken ct)
        => Ok(ApiResponse<OrderingFinanceOperationDto?>.Ok(await service.GetOperationAsync(sessionId, operationId, ct)));

    [HttpGet("period-options")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderingFinancePeriodOptionDto>>>> PeriodOptions(
        string sessionId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<OrderingFinancePeriodOptionDto>>.Ok(await service.GetPeriodOptionsAsync(sessionId, ct)));

    [HttpGet("admission")]
    public async Task<ActionResult<ApiResponse<OrderingAdmissionDto?>>> Admission(string sessionId, CancellationToken ct)
        => Ok(ApiResponse<OrderingAdmissionDto?>.Ok(await service.GetAdmissionAsync(sessionId, ct)));

    [HttpPost("admission")]
    public async Task<ActionResult<ApiResponse<OrderingAdmissionOperationDto>>> SaveAdmission(string sessionId,
        SaveOrderingAdmissionRequest request, CancellationToken ct)
        => Ok(ApiResponse<OrderingAdmissionOperationDto>.Ok(await service.SaveAdmissionAsync(sessionId, request, User, ct)));

    [HttpGet("cases")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderingFinanceCaseDto>>>> Cases(string sessionId,
        [FromQuery] bool includeResolved, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<OrderingFinanceCaseDto>>.Ok(await service.GetCasesAsync(sessionId, includeResolved, ct)));

    [HttpPost("cases/{caseId}/resolve")]
    public async Task<ActionResult<ApiResponse<OrderingFinanceCaseOperationDto>>> ResolveCase(string sessionId,
        string caseId, ResolveOrderingFinanceCaseRequest request, CancellationToken ct)
        => Ok(ApiResponse<OrderingFinanceCaseOperationDto>.Ok(await service.ResolveCaseAsync(sessionId, caseId, request, User, ct)));

    [HttpPost("departure")]
    public async Task<ActionResult<ApiResponse<OrderingSessionDepartureDto>>> Departure(string sessionId,
        UpdateOrderingSessionDepartureRequest request, CancellationToken ct)
        => Ok(ApiResponse<OrderingSessionDepartureDto>.Ok(await service.UpdateDepartureAsync(sessionId, request, User, ct)));

    [HttpGet("records/{recordId}/revisions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderingFinanceRevisionDto>>>> Revisions(
        string sessionId, string recordId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<OrderingFinanceRevisionDto>>.Ok(await service.GetRevisionsAsync(sessionId, recordId, ct)));

    [HttpPost("records")]
    public async Task<ActionResult<ApiResponse<OrderingFinanceOperationDto>>> Create(string sessionId,
        SaveOrderingFinanceRecordRequest request, CancellationToken ct)
        => Ok(ApiResponse<OrderingFinanceOperationDto>.Ok(await service.SaveAsync(sessionId, null, request, User, ct)));

    [HttpPut("records/{recordId}")]
    public async Task<ActionResult<ApiResponse<OrderingFinanceOperationDto>>> CorrectOrConfirm(string sessionId,
        string recordId, SaveOrderingFinanceRecordRequest request, CancellationToken ct)
        => Ok(ApiResponse<OrderingFinanceOperationDto>.Ok(await service.SaveAsync(sessionId, recordId, request, User, ct)));
}
