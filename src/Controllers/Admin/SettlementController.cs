using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Admin.Settlement;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/settlement")]
public sealed class SettlementController : ControllerBase
{
    private readonly ISettlementService _service;

    public SettlementController(ISettlementService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> Get(
        [FromQuery] DateOnly businessDate, [FromQuery] int sessionNo = 1, CancellationToken cancellationToken = default)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.GetOverviewAsync(businessDate, sessionNo, cancellationToken)));

    [HttpGet("rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SettlementRuleDto>>>> Rules(
        [FromQuery] string? dayType, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<SettlementRuleDto>>.Ok(await _service.GetRulesAsync(dayType, cancellationToken)));

    [HttpPost("rules")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementRuleDto>>> SaveRule(
        SaveSettlementRuleRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementRuleDto>.Ok(await _service.SaveRuleAsync(request, User, cancellationToken)));

    [HttpPut("inputs")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> SaveInputs(
        SaveSettlementInputsRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.SaveInputsAsync(request, User, cancellationToken)));

    [HttpPost("calculate")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> Calculate(
        SettlementCalculateRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.CalculateAsync(request, User, cancellationToken)));

    [HttpPost("finalize")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> Finalize(
        SettlementFinalizeRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.FinalizeAsync(request, User, cancellationToken)));

    [HttpPost("reopen")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> Reopen(
        SettlementReopenRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.ReopenAsync(request, User, cancellationToken)));

    [HttpPut("orders/{orderId}/amount")]
    [Authorize(Policy = "AdminManager")]
    public async Task<ActionResult<ApiResponse<SettlementOverviewDto>>> AdjustOrder(
        string orderId, SettlementOrderAdjustmentRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<SettlementOverviewDto>.Ok(await _service.SaveOrderAdjustmentAsync(orderId, request, User, cancellationToken)));
}
