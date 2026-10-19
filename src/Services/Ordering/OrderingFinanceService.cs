using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Ordering;

namespace ToBeClarify.Api.Services.Ordering;

public sealed class OrderingFinanceService(OrderingFinanceRepository repository)
{
    public Task<OrderingFinanceAccountDto> GetAccountAsync(string sessionId, CancellationToken ct)
        => repository.GetAccountAsync(sessionId, ct);

    public Task<OrderingFinanceOperationDto?> GetOperationAsync(string sessionId, string operationId, CancellationToken ct)
        => repository.GetOperationAsync(sessionId, operationId, ct);

    public Task<IReadOnlyList<OrderingFinancePeriodOptionDto>> GetPeriodOptionsAsync(string sessionId, CancellationToken ct)
        => repository.GetPeriodOptionsAsync(sessionId, ct);

    public Task<IReadOnlyList<OrderingFinanceTipStaffOptionDto>> GetTipStaffOptionsAsync(string sessionId, CancellationToken ct)
        => repository.GetTipStaffOptionsAsync(sessionId, ct);

    public Task<IReadOnlyList<OrderingFinanceRevisionDto>> GetRevisionsAsync(string sessionId, string recordId, CancellationToken ct)
        => repository.GetRevisionsAsync(sessionId, recordId, ct);

    public Task<OrderingAdmissionDto?> GetAdmissionAsync(string sessionId, CancellationToken ct)
        => repository.GetAdmissionAsync(sessionId, ct);

    public Task<IReadOnlyList<OrderingFinanceCaseDto>> GetCasesAsync(string sessionId, bool includeResolved, CancellationToken ct)
        => repository.GetCasesAsync(sessionId, includeResolved, ct);

    public Task<OrderingCustomerBillDto> GetCustomerBillAsync(string sessionId, CancellationToken ct)
        => repository.GetCustomerBillAsync(sessionId, ct);

    public async Task<OrderingAdmissionOperationDto> SaveAdmissionAsync(string sessionId,
        SaveOrderingAdmissionRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        if (!Guid.TryParse(request.OperationId, out var op) || request.ExpectedVersion < 0)
            throw new BusinessException("操作識別碼或帳款版本無效。", "FINANCE_INVALID_OPERATION");
        if (request.Amount < 0 || request.DiscountAmount < 0 || request.DiscountAmount > request.Amount || request.CreditAmount < 0 || request.Amount > 1_000_000_000_000L)
            throw new BusinessException("入場費、折讓與折抵額度無效。", "ADMISSION_INVALID_AMOUNT");
        if (request.Mode is not ("received" or "unpaid" or "waived" or "reissue"))
            throw new BusinessException("入場狀態無效。", "ADMISSION_INVALID_MODE");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "入場帳記錄" : request.Reason.Trim();
        if (reason.Length > 500) throw new BusinessException("原因最多 500 字。", "FINANCE_REASON_REQUIRED");
        var normalized = request with { OperationId = op.ToString(), Reason = reason,
            DiscountAmount = request.Mode == "waived" ? request.Amount : request.DiscountAmount,
            CashPeriodId = EmptyToNull(request.CashPeriodId) };
        return await repository.SaveAdmissionAsync(sessionId, normalized, Hash(new { sessionId, request = normalized }), actorId, ct);
    }

    public async Task<OrderingFinanceCaseOperationDto> ResolveCaseAsync(string sessionId, string caseId,
        ResolveOrderingFinanceCaseRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        if (!Guid.TryParse(request.OperationId, out var op) || request.ExpectedVersion < 0)
            throw new BusinessException("操作識別碼或帳款版本無效。", "FINANCE_INVALID_OPERATION");
        var normalized = request with { OperationId = op.ToString(), CashPeriodId = EmptyToNull(request.CashPeriodId),
            ResolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote) ? "案件確認完成" : request.ResolutionNote.Trim() };
        return await repository.ResolveCaseAsync(sessionId, caseId, normalized,
            Hash(new { sessionId, caseId, request = normalized }), actorId, ct);
    }

    public async Task<OrderingSessionDepartureDto> UpdateDepartureAsync(string sessionId,
        UpdateOrderingSessionDepartureRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        if (!Guid.TryParse(request.OperationId, out var op))
            throw new BusinessException("操作識別碼無效。", "FINANCE_INVALID_OPERATION");
        var action = (request.Action ?? "depart").Trim().ToLowerInvariant();
        var normalized = request with { OperationId = op.ToString(), Action = action,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "顧客離店處理" : request.Reason.Trim() };
        return await repository.UpdateDepartureAsync(sessionId, normalized,
            Hash(new { sessionId, request = normalized }), actorId, ct);
    }

    public async Task<OrderingFinanceOperationDto> SaveAsync(string sessionId, string? recordId,
        SaveOrderingFinanceRecordRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedException();
        if (!Guid.TryParse(request.OperationId, out var operationId) || request.ExpectedVersion < 0)
            throw new BusinessException("操作識別碼或帳款版本無效，請重新讀取帳單。", "FINANCE_INVALID_OPERATION");
        if (request.Kind is not ("charge_add" or "charge_reduce" or "cash_receipt" or "cash_refund" or "tip_supplement"))
            throw new BusinessException("請選擇加收、折讓、實收、實退或補登小費。", "FINANCE_INVALID_KIND");
        // Cash events use the void workflow instead of an active zero-value record.
        var allowsZeroCorrection = recordId is not null && request.Kind is ("charge_add" or "charge_reduce");
        if (request.Amount < (allowsZeroCorrection ? 0 : 1) || request.Amount > 1_000_000_000_000L)
            throw new BusinessException("請填入有效的整數金額。", "FINANCE_INVALID_AMOUNT");
        if (request.SourceKind is not ("admission" or "order" or "item" or "unallocated") ||
            request.AllocationStatus is not ("pending" or "confirmed") ||
            request.HoldScope is not ("none" or "session" or "order" or "item"))
            throw new BusinessException("費用歸屬或保留範圍無效。", "FINANCE_INVALID_ALLOCATION");
        if (request.OccurredAt == default || request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new BusinessException("請填寫實際發生時間，不能預先登記尚未發生的款項。", "FINANCE_INVALID_TIME");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new BusinessException("請填寫原因（最多 500 字）。", "FINANCE_REASON_REQUIRED");
        var isCash = request.Kind is "cash_receipt" or "cash_refund";
        var normalized = request with
        {
            OperationId = operationId.ToString(), Reason = request.Reason.Trim(),
            OrderId = EmptyToNull(request.OrderId), OrderItemId = EmptyToNull(request.OrderItemId),
            SourcePeriodId = EmptyToNull(request.SourcePeriodId),
            CashPeriodId = isCash ? EmptyToNull(request.CashPeriodId) : null,
            ReversesRecordId = EmptyToNull(request.ReversesRecordId),
            LinkedCashRecordId = EmptyToNull(request.LinkedCashRecordId),
            OccurredAt = request.OccurredAt.ToOffset(TimeSpan.FromHours(8)),
            // Unallocated events always retain the entire affected account's variable revenue.
            AllocationStatus = request.Kind == "tip_supplement" ? "confirmed"
                : request.SourceKind == "unallocated" ? "pending" : request.AllocationStatus,
            HoldScope = request.Kind == "tip_supplement" ? "none"
                : request.SourceKind == "unallocated" ? "session"
                : request.AllocationStatus == "pending" && request.HoldScope == "none" ? "session" : request.HoldScope
        };
        if (normalized.SourceKind is "order" or "item" && normalized.OrderId is null ||
            normalized.SourceKind == "item" && normalized.OrderItemId is null ||
            normalized.SourceKind is "admission" or "unallocated" &&
                (normalized.OrderId is not null || normalized.OrderItemId is not null) ||
            normalized.SourceKind == "order" && normalized.OrderItemId is not null ||
            normalized.HoldScope == "item" && normalized.OrderItemId is null ||
            normalized.HoldScope == "order" && normalized.OrderId is null)
            throw new BusinessException("請指定符合範圍的原訂單／項目。", "FINANCE_SOURCE_REQUIRED");
        if (request.Kind == "tip_supplement" && normalized.SourceKind is not ("order" or "item"))
            throw new BusinessException("補登小費必須歸屬至一張訂單或訂單項目。", "FINANCE_TIP_SOURCE_REQUIRED");
        if (!isCash && normalized.ReversesRecordId is not null)
            throw new BusinessException("只有實際收退款能關聯反向款項。", "FINANCE_INVALID_REVERSAL");
        if (request.TipAllocations?.Any(allocation => allocation is null) == true)
            throw new BusinessException("小費分配資料無效。", "FINANCE_TIP_ALLOCATION_INVALID");
        var requestedTipAllocations = request.TipAllocations?.Select(allocation => allocation with
        {
            StaffId = allocation.StaffId?.Trim() ?? ""
        }).ToArray() ?? [];
        var tipMode = request.TipDistributionMode?.Trim().ToLowerInvariant();
        normalized = normalized with { TipDistributionMode = tipMode, TipAllocations = requestedTipAllocations };
        var tipAllocations = request.Kind == "tip_supplement"
            ? OrderingTipAllocationCalculator.Calculate(request.Amount, normalized.TipDistributionMode ?? "", requestedTipAllocations)
            : Array.Empty<CalculatedOrderingFinanceTipAllocation>();
        if (request.Kind != "tip_supplement" &&
            (!string.IsNullOrWhiteSpace(request.TipDistributionMode) || requestedTipAllocations.Length > 0 || normalized.LinkedCashRecordId is not null))
            throw new BusinessException("只有補登小費能設定分配方式或關聯收款。", "FINANCE_TIP_ALLOCATION_INVALID");
        var payload = JsonSerializer.Serialize(new { sessionId, recordId, request = normalized });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return await repository.SaveAsync(sessionId, recordId, normalized, tipAllocations, hash, actorId, ct);
    }

    public Task<OrderingFinanceOperationDto> VoidAsync(string sessionId, string recordId,
        VoidOrderingFinanceRecordRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        if (!Guid.TryParse(request.OperationId, out var operationId) || request.ExpectedAccountVersion < 0 ||
            request.ExpectedRecordVersion < 1)
            throw new BusinessException("操作識別碼或帳款版本無效，請重新讀取。", "FINANCE_INVALID_OPERATION");
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length == 0 || reason.Length > 500)
            throw new BusinessException("請填寫作廢原因（最多 500 字）。", "FINANCE_VOID_REASON_REQUIRED");
        var normalized = request with { OperationId = operationId.ToString(), Reason = reason };
        return repository.VoidAsync(sessionId, recordId, normalized,
            Hash(new { sessionId, recordId, request = normalized }), actorId, ct);
    }

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedException();

    private static string Hash(object payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
