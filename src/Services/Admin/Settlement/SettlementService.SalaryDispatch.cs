using System.Security.Claims;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Settlement;

public sealed partial class SettlementService
{
    public Task<SettlementSalaryDispatchDto> GetSalaryDispatchAsync(DateOnly businessDate, int sessionNo,
        CancellationToken cancellationToken)
    {
        ValidateDate(businessDate);
        if (sessionNo < 1) throw new BusinessException("營業時段必須大於零。", "SETTLEMENT_SESSION_INVALID");
        return _repository.GetSalaryDispatchAsync(businessDate, sessionNo, cancellationToken);
    }

    public Task SaveSalaryDispatchAsync(SaveSettlementSalaryDispatchRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateDate(request.BusinessDate);
        if (request.SessionNo < 1 || !Guid.TryParseExact(request.SettlementId, "D", out _)
            || !Guid.TryParseExact(request.StaffId, "D", out _) || !Guid.TryParseExact(request.OperationId, "D", out _)
            || request.IsPaid is null || request.ExpectedVersion is null or < 0
            || request.ExpectedCorrectionVersion is null or < 0 || request.ExpectedInputVersion is null or < 0)
            throw new BusinessException("薪資派發資料或版本無效，請重新載入。", "SETTLEMENT_DISPATCH_INVALID");
        return _repository.SaveSalaryDispatchAsync(request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }
}
