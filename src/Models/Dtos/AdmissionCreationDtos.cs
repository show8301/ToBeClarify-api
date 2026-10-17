using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record AdmissionPolicyDto(long? AdmissionAmount, long Version,
    int MealCreditAmount, string AmountSource, string CreditSource);

public sealed record SaveAdmissionPolicyRequest
{
    [JsonRequired, Range(0, 1_000_000_000_000L)] public long? AdmissionAmount { get; init; }
    [JsonRequired, Range(0, long.MaxValue)] public long ExpectedVersion { get; init; }
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; init; } = "";
}

public sealed record AdmissionPreviewDto(int ContractVersion, string BusinessPeriodId,
    DateOnly BusinessDate, int PeriodVersion, long PolicyVersion, long AdmissionAmount,
    int MealCreditAmount, DateTimeOffset ExpiresAt, string PricingRevision,
    bool ConfirmedCreationAvailable, bool LegacyCreationAllowed);

public sealed record AdmissionConfirmation
{
    [Required] public string BusinessPeriodId { get; init; } = "";
    [Required, RegularExpression("^[a-f0-9]{64}$")] public string PricingRevision { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    [RegularExpression("^(received|unpaid|waived)$")] public string Mode { get; init; } = "received";
    [Range(0, 1_000_000_000_000L)] public long DiscountAmount { get; init; }
    public bool PaymentConfirmed { get; init; }
    [Required, Range(0, 1_000_000_000_000L)] public long? ConfirmedReceivedAmount { get; init; }
    [StringLength(500)] public string? Reason { get; init; }
}

// Confirmed creation POST /api/admin/order-sessions/with-admission.
// It deliberately has a separate route: old Web requests never imply payment.
public sealed record CreateAdmissionSessionRequest
{
    [Required] public Guid? OperationId { get; init; }
    [Required, StringLength(100, MinimumLength = 1)] public string GameId { get; init; } = "";
    [Required, StringLength(16, MinimumLength = 1)] public string ServerName { get; init; } = "";
    [StringLength(100)] public string? CustomerName { get; init; }
    [StringLength(40)] public string? CustomerUid { get; init; }
    [Range(0, 100)] public int? MaxNominatedStaff { get; init; }
    [Required] public AdmissionConfirmation? Admission { get; init; }
}

// No order token, short code or recovery code belongs in this durable result.
public sealed record AdmissionCreationResultDto(Guid OperationId, string SessionId,
    string AdmissionId, string BusinessPeriodId, DateOnly BusinessDate,
    long AdmissionAmount, long DiscountAmount, int MealCreditAmount,
    string AdmissionStatus, long ReceivedAmount, string? ChargeRecordId,
    string? ReceiptRecordId, string? CashPeriodId, long AccountVersion,
    DateTimeOffset RecordedAt, string RecordedBy, string CredentialAction);

public sealed record AdmissionCreationIssuedDto(OrderSessionIssuedDto? Issued,
    AdmissionCreationResultDto Result);
