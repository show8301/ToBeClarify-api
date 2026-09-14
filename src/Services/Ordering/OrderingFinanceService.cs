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

    public Task<IReadOnlyList<OrderingFinanceRevisionDto>> GetRevisionsAsync(string sessionId, string recordId, CancellationToken ct)
        => repository.GetRevisionsAsync(sessionId, recordId, ct);

    public Task<OrderingFinanceOperationDto> SaveAsync(string sessionId, string? recordId,
        SaveOrderingFinanceRecordRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedException();
        if (!Guid.TryParse(request.OperationId, out var operationId) || request.ExpectedVersion < 0)
            throw new BusinessException("操作識別碼或帳款版本無效，請重新讀取帳單。", "FINANCE_INVALID_OPERATION");
        if (request.Kind is not ("charge_add" or "charge_reduce" or "cash_receipt" or "cash_refund"))
            throw new BusinessException("請選擇加收、折讓、實收或實退。", "FINANCE_INVALID_KIND");
        // Gil is an integer. Zero is permitted only for correcting an erroneously recorded event.
        if (request.Amount < (recordId is null ? 1 : 0) || request.Amount > 1_000_000_000_000L)
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
            OccurredAt = request.OccurredAt.ToOffset(TimeSpan.FromHours(8)),
            // Unallocated events always retain the entire affected account's variable revenue.
            AllocationStatus = request.SourceKind == "unallocated" ? "pending" : request.AllocationStatus,
            HoldScope = request.SourceKind == "unallocated" ? "session"
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
        if (!isCash && normalized.ReversesRecordId is not null)
            throw new BusinessException("只有實際收退款能關聯反向款項。", "FINANCE_INVALID_REVERSAL");
        var payload = JsonSerializer.Serialize(new { sessionId, recordId, request = normalized });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return repository.SaveAsync(sessionId, recordId, normalized, hash, actorId, ct);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
