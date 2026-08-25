namespace ToBeClarify.Api.Models.Entities;

public sealed class SiteSettingRow
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = "{}";
    public string? Description { get; set; }
}

public sealed class NavigationItemRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string RoutePath { get; set; } = string.Empty;
    public string Placement { get; set; } = string.Empty;
    public string? ParentItemId { get; set; }
    public bool IsDropdown { get; set; }
    public int SortOrder { get; set; }
}

public sealed class HomeEventCarouselRow
{
    public string Id { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string? EventTimeSnapshot { get; set; }
    public string? CtaLabel { get; set; }
    public bool AlbumExists { get; set; }
}

public sealed class HomeSlideRow
{
    public string Id { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public int DisplaySeconds { get; set; }
}

public sealed class ShopRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string RuleText { get; set; } = string.Empty;
    public string? RuleNote { get; set; }
}

public sealed class PricingRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PriceText { get; set; }
}

public sealed class StaffRow
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? AvatarMediaId { get; set; }
    public string? RoleTitle { get; set; }
    public string? ShortBio { get; set; }
    public string? ProfileBio { get; set; }
    public bool IsWorkingToday { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string? StatusText { get; set; }
    public string? TodayShift { get; set; }
    public bool IsNominatable { get; set; }
}

public sealed class StaffServiceRow
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public string? PriceText { get; set; }
    public int? Price { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsNominatable { get; set; }
    public int? AdditionalPersonPrice { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StaffGalleryItemRow
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class GalleryAlbumRow
{
    public string Id { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string? AlbumDescription { get; set; }
    public string? CoverMediaId { get; set; }
    public string? PeriodText { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? DetailContent { get; set; }
}

public sealed class GalleryItemRow
{
    public string Id { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string? Title { get; set; }
    public string? Caption { get; set; }
    public DateTime? ShotAt { get; set; }
}

public sealed class GuestbookCommentRow
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GuestbookReplyRow
{
    public string Id { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class MenuCategoryRow
{
    public string Id { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryDescription { get; set; }
}

public sealed class MenuItemRow
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemDescription { get; set; }
    public int Price { get; set; }
    public string? MediaId { get; set; }
    public string? Tags { get; set; }
}

public sealed class MenuSetRow
{
    public string Id { get; set; } = string.Empty;
    public string SetName { get; set; } = string.Empty;
    public string? SetDescription { get; set; }
    public int SetPrice { get; set; }
    public string? MediaId { get; set; }
}

public sealed class MenuSetItemRow
{
    public string Id { get; set; } = string.Empty;
    public string SetId { get; set; } = string.Empty;
    public string MenuItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemRole { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class StaffReservationRow
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffNameSnapshot { get; set; } = string.Empty;
    public string? StaffAvatarMediaId { get; set; }
    public string? LegacyStaffAvatarSnapshot { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? ServiceLabel { get; set; }
    public string? CustomerName { get; set; }
}

public sealed class RankingRow
{
    public string Id { get; set; } = string.Empty;
    public string RankingType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string DisplayNameSnapshot { get; set; } = string.Empty;
    public string? AvatarMediaId { get; set; }
    public string? LegacyAvatarSnapshot { get; set; }
    public string? TitleBadge { get; set; }
    public int RankPosition { get; set; }
    public int ScoreValue { get; set; }
    public string? ScoreLabel { get; set; }
    public string? PeriodLabel { get; set; }
}
