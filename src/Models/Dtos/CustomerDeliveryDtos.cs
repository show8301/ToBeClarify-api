using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record CustomerProfileDto(string Uid, string DisplayName, DateTime CreatedAt);
public sealed record CustomerIdentityCandidateDto(
    string Uid,
    string DisplayName,
    DateTime CreatedAt,
    DateTime? LastVisitAt,
    int VisitCount,
    int OrderCount,
    long OrderAmount);
public sealed record CustomerIdentityCandidatesDto(string GameId, IReadOnlyList<CustomerIdentityCandidateDto> Items);
public sealed record CustomerHistoryPage(int Page, int PageSize, int TotalCount, IReadOnlyList<CustomerVisitDto> Items);
public sealed class CustomerVisitDto
{
    public string SessionId { get; set; } = "";
    public string? CustomerUid { get; set; }
    public string GameId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime BusinessDate { get; set; }
    public string? BusinessPeriodId { get; set; }
    public string SessionStatus { get; set; } = "";
    public string EntryStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? RecoveryCodeIssuedAt { get; set; }
    public int RecoveryCodeVersion { get; set; }
    public bool HasRecoveryCode { get; set; }
    public int OrderCount { get; set; }
    public long OrderAmount { get; set; }
    public long NetReceived { get; set; }
    public bool HasCashRecords { get; set; }
    public int PendingDeliveryCount { get; set; }
    public int GameIdVisitCount { get; set; }
    public long GameIdOrderAmount { get; set; }
    public IReadOnlyList<CustomerHistoryOrderDto> Orders { get; set; } = [];
}
public sealed record CustomerHistoryOrderDto(string Id, string OrderNumber, string Status, long TotalAmount);
public sealed record CustomerSummaryDto(int VisitCount, int OrderCount, long OrderAmount, long NetReceived, int PendingDeliveryCount);
public sealed record CustomerDetailDto(CustomerProfileDto Profile, CustomerSummaryDto Summary, IReadOnlyList<CustomerVisitDto> Visits);
public sealed class LinkCustomerProfileRequest
{
    [StringLength(40)] public string? CustomerUid { get; init; }
}
public sealed class DeliveryAccessRequest
{
    [StringLength(100)] public string? ClaimCode { get; init; }
    [StringLength(40)] public string? CustomerUid { get; init; }
    [Range(1, 100000)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 20;
}
public sealed class CreateArtDeliveryRequest
{
    [Required, StringLength(36)] public string SessionId { get; init; } = "";
    [StringLength(36)] public string? OrderId { get; init; }
    [StringLength(36)] public string? OrderItemId { get; init; }
    [Required, StringLength(160)] public string Title { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    public DateOnly? DueDate { get; init; }
}
public sealed class UpdateArtDeliveryRequest
{
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [Required, StringLength(160)] public string Title { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    [Required, RegularExpression("^(pending|in_progress|ready|delivered|cancelled)$")] public string Status { get; init; } = "pending";
    public DateOnly? DueDate { get; init; }
}
public sealed class AddDeliveryLinkRequest
{
    [Required, StringLength(160)] public string Label { get; init; } = "";
    [Required, StringLength(2048)] public string Url { get; init; } = "";
}
public sealed class ArtDeliveryDto
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string? CustomerUid { get; set; }
    public string GameId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime BusinessDate { get; set; }
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? OrderItemId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? DueDate { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public IReadOnlyList<ArtDeliveryAssetDto> Assets { get; set; } = [];
}
public sealed class ArtDeliveryAssetDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Url { get; set; }
    public string? ContentType { get; set; }
    public long ByteSize { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed record ArtDeliveryIssuedDto(ArtDeliveryDto Delivery, string ClaimCode);
public sealed record PublicDeliveryListDto(IReadOnlyList<PublicArtDeliveryDto> Items, int Page, int PageSize, bool HasMore);
public sealed record PublicArtDeliveryDto(string Id, string Title, string? Description, string Status,
    DateTime? DueDate, DateTime CreatedAt, DateTime? DeliveredAt, IReadOnlyList<ArtDeliveryAssetDto> Assets);
