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
    string? ReversesRecordId, DateTimeOffset CreatedAt, string CreatedBy,
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
