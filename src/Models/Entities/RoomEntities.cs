namespace ToBeClarify.Api.Models.Entities;

public sealed class RoomRow
{
    public string Id { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string? DetailContent { get; set; }
    public string? OwnerStaffId { get; set; }
    public string? OwnerStaffName { get; set; }
    public int SegmentPrice { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RoomPhotoRow
{
    public string Id { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class RoomStatusRow
{
    public string Id { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string? OwnerStaffId { get; set; }
    public string? OwnerStaffName { get; set; }
    public int SegmentPrice { get; set; }
    public string CurrentStatus { get; set; } = "available";
}

public sealed class RoomProfitSharingSettingsRow
{
    public string Id { get; set; } = "default";
    public int CommonRoomStaffPercentage { get; set; }
    public int DedicatedRoomStaffPercentage { get; set; }
}

public sealed class RoomServiceOrderRow
{
    public string Id { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? OrderItemId { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string RoomNameSnapshot { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int SegmentCount { get; set; }
    public int SegmentMinutesSnapshot { get; set; }
    public int UnitPrice { get; set; }
    public int TotalAmount { get; set; }
    public string OrderStatus { get; set; } = "scheduled";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
