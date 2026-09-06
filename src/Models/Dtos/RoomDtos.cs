using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record RoomPhotoDto(string Id, string? MediaId, string ImageUrl, int SortOrder);

public sealed record RoomDto(
    string Id,
    string RoomName,
    string ShortDescription,
    string? DetailContent,
    string OwnershipType,
    string? OwnerStaffId,
    string? OwnerStaffName,
    int SegmentMinutes,
    int SegmentPrice,
    IReadOnlyList<RoomPhotoDto> Photos);

public sealed record RoomListDto(int SegmentMinutes, IReadOnlyList<RoomDto> Rooms);

public sealed record RoomStatusDto(
    string Id,
    string RoomName,
    string OwnershipType,
    string? OwnerStaffName,
    string CurrentStatus,
    string StatusText);

public sealed record AdminRoomDto(
    string Id,
    string RoomName,
    string ShortDescription,
    string? DetailContent,
    string OwnershipType,
    string? OwnerStaffId,
    string? OwnerStaffName,
    int SegmentMinutes,
    int SegmentPrice,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<RoomPhotoDto> Photos);

public sealed class SaveRoomRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string RoomName { get; init; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 1)]
    public string ShortDescription { get; init; } = string.Empty;

    public string? DetailContent { get; init; }

    [StringLength(36)]
    public string? OwnerStaffId { get; init; }

    [Range(0, 100000000)]
    public int? SegmentPrice { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public List<SaveRoomPhotoRequest> Photos { get; init; } = [];
}

public sealed class SaveRoomPhotoRequest
{
    [StringLength(36)]
    public string? Id { get; init; }

    [Required, StringLength(36)]
    public string MediaId { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public sealed record RoomProfitSharingSettingsDto(
    int CommonRoomStaffPercentage,
    int DedicatedRoomStaffPercentage);

public sealed class SaveRoomProfitSharingSettingsRequest
{
    [Range(0, 100)]
    public int CommonRoomStaffPercentage { get; init; }

    [Range(0, 100)]
    public int DedicatedRoomStaffPercentage { get; init; }
}

public sealed record RoomServiceOrderDto(
    string Id,
    string RoomId,
    string RoomName,
    DateTime BusinessDate,
    DateTime StartsAt,
    DateTime EndsAt,
    int SegmentCount,
    int SegmentMinutes,
    int UnitPrice,
    int TotalAmount,
    string Status,
    string? Note,
    DateTime CreatedAt);

public sealed class CreateRoomServiceOrderRequest
{
    [Required, StringLength(36)]
    public string RoomId { get; init; } = string.Empty;

    [Required]
    public DateTime BusinessDate { get; init; }

    [Required]
    public DateTime StartsAt { get; init; }

    [Range(1, 72)]
    public int SegmentCount { get; init; } = 1;

    [StringLength(500)]
    public string? Note { get; init; }
}

public sealed class UpdateRoomServiceOrderStatusRequest
{
    [Required, RegularExpression("^(scheduled|in_service|completed|cancelled)$")]
    public string Status { get; init; } = string.Empty;
}
