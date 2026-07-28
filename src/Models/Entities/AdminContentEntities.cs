namespace ToBeClarify.Api.Models.Entities;

public sealed class AdminSiteSettingRow
{
    public string Id { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = "{}";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AdminNavigationItemRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string RoutePath { get; set; } = string.Empty;
    public string Placement { get; set; } = string.Empty;
    public string? ParentItemId { get; set; }
    public int SortOrder { get; set; }
    public bool IsDropdown { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminHomeCarouselRow
{
    public string Id { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public string OverrideTitle { get; set; } = string.Empty;
    public string OverrideSummary { get; set; } = string.Empty;
    public string? OverrideMediaId { get; set; }
    public string? EventTimeSnapshot { get; set; }
    public string? CtaLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminHomeSlideRow
{
    public string Id { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminShopRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string RuleText { get; set; } = string.Empty;
    public string? RuleNote { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminStaffMemberRow
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
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AdminGalleryAlbumRow
{
    public string Id { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string? AlbumDescription { get; set; }
    public string? CoverMediaId { get; set; }
    public string? PeriodText { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? DetailContent { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
}

public sealed class AdminGalleryItemRow
{
    public string Id { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string? Title { get; set; }
    public string? Caption { get; set; }
    public DateTime? ShotAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
}

public sealed class AdminStaffServiceRow
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public string? PriceText { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminStaffGalleryItemRow
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
}

public sealed class AdminPricingRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PriceText { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminMenuCategoryRow
{
    public string Id { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryDescription { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class AdminMenuItemRow
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemDescription { get; set; }
    public int Price { get; set; }
    public string? MediaId { get; set; }
    public string? Tags { get; set; }
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class AdminMenuSetRow
{
    public string Id { get; set; } = string.Empty;
    public string SetName { get; set; } = string.Empty;
    public string? SetDescription { get; set; }
    public int SetPrice { get; set; }
    public string? MediaId { get; set; }
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class AdminMenuSetItemRow
{
    public string Id { get; set; } = string.Empty;
    public string SetId { get; set; } = string.Empty;
    public string MenuItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemRole { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int SortOrder { get; set; }
}
