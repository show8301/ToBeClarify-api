using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public interface ISettlementService
{
    Task<SettlementOverviewDto> GetOverviewAsync(DateOnly businessDate, int sessionNo,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SettlementRuleDto>> GetRulesAsync(string? dayType, CancellationToken cancellationToken);
    Task<SettlementRuleDto> SaveRuleAsync(SaveSettlementRuleRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<SettlementOverviewDto> SaveInputsAsync(SaveSettlementInputsRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<SettlementOverviewDto> CalculateAsync(SettlementCalculateRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<SettlementOverviewDto> FinalizeAsync(SettlementFinalizeRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<SettlementOverviewDto> ReopenAsync(SettlementReopenRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<SettlementOverviewDto> SubmitAttendanceBackfillAsync(SettlementAttendanceBackfillRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<SettlementOverviewDto> ReviewAttendanceBackfillAsync(string requestId,
        SettlementAttendanceReviewRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<SettlementOverviewDto> SaveOrderAdjustmentAsync(string orderId, SettlementOrderAdjustmentRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken);
}
