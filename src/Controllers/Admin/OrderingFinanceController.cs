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
