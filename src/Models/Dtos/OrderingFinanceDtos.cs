namespace ToBeClarify.Api.Models.Dtos;

public sealed record SaveOrderingFinanceRecordRequest
{
    public string OperationId { get; init; } = "";
    public long ExpectedVersion { get; init; }
    public string Kind { get; init; } = "";
    public long Amount { get; init; }
    public string SourceKind { get; init; } = "unallocated";
    public string? OrderId { get; init; }
    public string? OrderItemId { get; init; }
    public string? SourcePeriodId { get; init; }
    public string? CashPeriodId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string AllocationStatus { get; init; } = "confirmed";
    public string HoldScope { get; init; } = "none";
    public string Reason { get; init; } = "";
    public string? ReversesRecordId { get; init; }
}

public sealed record OrderingFinanceRecordDto(
    string Id, long Version, string Kind, long Amount, string SourceKind,
    string? OrderId, string? OrderItemId, string SourcePeriodId, string? CashPeriodId,
    DateTimeOffset OccurredAt, string AllocationStatus, string HoldScope, string Reason,
    string? ReversesRecordId, string? CaseId, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset UpdatedAt, string UpdatedBy, DateTimeOffset? ConfirmedAt, string? ConfirmedBy);

public sealed record OrderingFinanceAccountDto(
    string SessionId, string? SourcePeriodId, string BusinessDate, int FlowVersion, long Version,
    bool CanWrite, bool HasRecordedCash, bool AdmissionChargeRecorded,
    long OrderReceivable, long ChargeAdditions, long ChargeReductions, long Receivable,
    long CashReceived, long CashRefunded, long NetCash, long Balance,
    long RefundDue, long UnassignedNetCash, int PendingCount, bool BalanceIsProvisional,
    string HoldScope, IReadOnlyList<OrderingFinanceRecordDto> Records);

public sealed record OrderingFinanceOperationDto(string OperationId, string RecordId, long AccountVersion);
public sealed record OrderingFinanceRevisionDto(long Version, string OperationId,
    DateTimeOffset RecordedAt, string RecordedBy, OrderingFinanceRecordDto Record);
public sealed record OrderingFinancePeriodOptionDto(string Id, string BusinessDate);

public sealed record SaveOrderingAdmissionRequest
{
    public string OperationId { get; init; } = "";
    public long ExpectedVersion { get; init; }
    public long Amount { get; init; }
    public long DiscountAmount { get; init; }
    public long CreditAmount { get; init; }
    public string Mode { get; init; } = "received";
    public string? CashPeriodId { get; init; }
    public string Reason { get; init; } = "";
}

public sealed record OrderingAdmissionDto(
    string Id, string SessionId, long Amount, long DiscountAmount, long CreditAmount,
    string Status, string? CashPeriodId, string? ChargeRecordId, string? ReceiptRecordId,
    long Version, string Reason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record OrderingAdmissionOperationDto(
    string OperationId, string AdmissionId, string? ChargeRecordId, string? ReceiptRecordId, long AccountVersion);

public sealed record OrderingFinanceCaseDto(
    string Id, string SessionId, string? RecordId, string? OrderId, string? OrderItemId,
    string CaseKind, long Amount, string ProfitScope, string Status, string SourcePeriodId,
    string Description, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset UpdatedAt,
    string UpdatedBy, DateTimeOffset? ResolvedAt, string? ResolvedBy, string? ResolutionNote);

public sealed record ResolveOrderingFinanceCaseRequest
{
    public string OperationId { get; init; } = "";
    public long ExpectedVersion { get; init; }
    public string? CashPeriodId { get; init; }
    public string ResolutionNote { get; init; } = "";
}

public sealed record OrderingFinanceCaseOperationDto(string OperationId, string CaseId, long AccountVersion);

public sealed record OrderingCustomerFinanceRecordDto(
    string Kind, long Amount, string SourceKind, string? OrderId, string? OrderItemId,
    DateTimeOffset OccurredAt, string AllocationStatus, string Reason, string? CaseId);

public sealed record OrderingCustomerBillDto(
    string SessionId, long AdmissionAmount, long AdmissionDiscount, long AdmissionCredit,
    string AdmissionStatus, long OrderReceivable, long ChargeAdditions, long ChargeReductions,
    long Receivable, long CashReceived, long CashRefunded, long NetCash, long Balance,
    long RefundDue, int PendingCount, bool HasUnresolvedCase,
    IReadOnlyList<OrderingCustomerFinanceRecordDto> Records);

public sealed record UpdateOrderingSessionDepartureRequest
{
    public string OperationId { get; init; } = "";
    public string Action { get; init; } = "depart";
    public string Reason { get; init; } = "";
}

public sealed record OrderingSessionDepartureDto(string SessionId, string EntryStatus, string SessionStatus,
    bool CanOrder, DateTimeOffset? DepartedAt, string? Reason);
