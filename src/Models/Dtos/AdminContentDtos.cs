using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record AdminSiteSettingDto(
    string Id,
    string SettingKey,
    JsonElement SettingValue,
    string? Description,
    bool IsActive);

public sealed class SaveSiteSettingRequest
{
    public JsonElement SettingValue { get; init; }

    [StringLength(255)]
    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record AdminNavigationItemDto(
    string Id,
    string Label,
    string RoutePath,
    string Placement,
    string? ParentItemId,
    int SortOrder,
    bool IsDropdown,
    bool IsEnabled);

public sealed class SaveNavigationItemRequest
{
    [Required, StringLength(40, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;

    [Required, StringLength(120)]
    public string RoutePath { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string Placement { get; init; } = "both";

    [StringLength(40)]
    public string? ParentItemId { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsDropdown { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed record AdminHomeCarouselDto(
    string Id,
    string? AlbumId,
    string OverrideTitle,
    string OverrideSummary,
    string? OverrideMediaId,
    string? OverrideImageUrl,
    string? EventTimeSnapshot,
    string? CtaLabel,
    int SortOrder,
    bool IsEnabled);

public sealed class SaveHomeCarouselRequest
{
    [StringLength(40)]
    public string? AlbumId { get; init; }

    [Required, StringLength(120)]
    public string OverrideTitle { get; init; } = string.Empty;

    [Required, StringLength(255)]
    public string OverrideSummary { get; init; } = string.Empty;

    [StringLength(40)]
    public string? OverrideMediaId { get; init; }

    [StringLength(500)]
    public string? OverrideImageUrl { get; init; }

    [StringLength(80)]
    public string? EventTimeSnapshot { get; init; }

    [StringLength(40)]
    public string? CtaLabel { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed record AdminHomeSlideDto(
    string Id,
    string? MediaId,
    string? ImageUrl,
    int SortOrder,
    bool IsEnabled,
    int DisplaySeconds);

public sealed class SaveHomeSlideRequest
{
    [StringLength(40)]
    public string? MediaId { get; init; }

    [StringLength(500)]
    public string? ImageUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;

    [Range(1, 60)]
    public int DisplaySeconds { get; init; } = 10;
}

public sealed class UpdateStaffMemberStatusRequest
{
    public bool? IsWorkingToday { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record AdminShopRuleDto(
    string Id,
    string RuleText,
    string? RuleNote,
    int SortOrder,
    bool IsEnabled);

public sealed class SaveShopRuleRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string RuleText { get; init; } = string.Empty;

    [StringLength(255)]
    public string? RuleNote { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed record AdminStaffServiceDto(
    string Id,
    string StaffId,
    string ServiceType,
    string ServiceName,
    string ServiceDescription,
    string? PriceText,
    int SortOrder,
    bool IsEnabled);

public sealed record AdminStaffGalleryItemDto(
    string Id,
    string StaffId,
    string? MediaId,
    string? ImageUrl,
    int SortOrder,
    bool IsPublished);

public sealed record AdminStaffMemberDto(
    string Id,
    string DisplayName,
    string? Nickname,
    string? AvatarMediaId,
    string? AvatarUrl,
    string? RoleTitle,
    string? ShortBio,
    string? ProfileBio,
    bool IsWorkingToday,
    string CurrentStatus,
    string? StatusText,
    string? TodayShift,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<AdminStaffServiceDto> Services,
    IReadOnlyList<AdminStaffGalleryItemDto> Gallery);

public sealed record AdminStaffMemberListItemDto(
    string Id,
    string DisplayName,
    string? AvatarMediaId,
    string? AvatarUrl,
    string? RoleTitle,
    bool IsWorkingToday,
    int SortOrder,
    bool IsActive);

public sealed class SaveStaffMemberRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    [StringLength(60)]
    public string? Nickname { get; init; }

    [StringLength(40)]
    public string? AvatarMediaId { get; init; }

    [StringLength(500)]
    public string? AvatarUrl { get; init; }

    [StringLength(80)]
    public string? RoleTitle { get; init; }

    [Required, StringLength(255, MinimumLength = 1)]
    public string? ShortBio { get; init; }

    public string? ProfileBio { get; init; }

    public bool IsWorkingToday { get; init; } = true;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    public bool IsActive { get; init; } = true;
    public List<SaveStaffServiceRequest> Services { get; init; } = [];
    public List<SaveStaffGalleryItemRequest> Gallery { get; init; } = [];
}

public sealed class ReorderStaffMembersRequest
{
    [Required, MinLength(1)]
    public List<ReorderStaffMemberItem> Items { get; init; } = [];
}

public sealed class ReorderStaffMemberItem
{
    [Required, StringLength(40)]
    public string Id { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public sealed class SaveStaffServiceRequest
{
    [StringLength(40)]
    public string? Id { get; init; }

    [Required, StringLength(20)]
    public string ServiceType { get; init; } = "special";

    [Required, StringLength(80, MinimumLength = 1)]
    public string ServiceName { get; init; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 1)]
    public string ServiceDescription { get; init; } = string.Empty;

    [StringLength(80)]
    public string? PriceText { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed class SaveStaffGalleryItemRequest
{
    [StringLength(40)]
    public string? Id { get; init; }

    [Required, StringLength(40)]
    public string? MediaId { get; init; }

    [StringLength(500)]
    public string? ImageUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsPublished { get; init; } = true;
}

public sealed record AdminGalleryAlbumDto(
    string Id,
    string AlbumTitle,
    string? AlbumDescription,
    string? CoverMediaId,
    string? CoverImageUrl,
    string? PeriodText,
    DateTime? EndsAt,
    string? DetailContent,
    IReadOnlyList<AdminGalleryItemDto> Items,
    int SortOrder,
    bool IsPublished);

public sealed record AdminGalleryItemDto(
    string Id,
    string? MediaId,
    string? ImageUrl,
    string? ThumbnailUrl,
    string? Title,
    string? Caption,
    DateTime? ShotAt,
    int SortOrder,
    bool IsPublished);

public sealed class SaveGalleryAlbumRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string AlbumTitle { get; init; } = string.Empty;

    [StringLength(500)]
    public string? AlbumDescription { get; init; }

    [StringLength(40)]
    public string? CoverMediaId { get; init; }

    [StringLength(500)]
    public string? CoverImageUrl { get; init; }

    [StringLength(80)]
    public string? PeriodText { get; init; }

    public DateTime? EndsAt { get; init; }

    public string? DetailContent { get; init; }

    public List<SaveGalleryItemRequest> Items { get; init; } = [];

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsPublished { get; init; }
}

public sealed class SaveGalleryItemRequest
{
    [StringLength(40)]
    public string? Id { get; init; }

    [StringLength(40)]
    public string? MediaId { get; init; }

    [StringLength(500)]
    public string? ImageUrl { get; init; }

    [StringLength(500)]
    public string? ThumbnailUrl { get; init; }

    [StringLength(100)]
    public string? Title { get; init; }

    [StringLength(500)]
    public string? Caption { get; init; }

    public DateTime? ShotAt { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsPublished { get; init; } = true;
}

public sealed record AdminPricingRuleDto(
    string Id,
    string Title,
    string Description,
    string? PriceText,
    int SortOrder,
    bool IsEnabled);

public sealed class SavePricingRuleRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    [StringLength(80)]
    public string? PriceText { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed record AdminMenuCategoryDto(
    string Id,
    string CategoryName,
    string? CategoryDescription,
    int SortOrder,
    bool IsEnabled,
    IReadOnlyList<AdminMenuItemDto> Items);

public sealed record AdminMenuItemDto(
    string Id,
    string CategoryId,
    string ItemName,
    string? ItemDescription,
    int Price,
    string? MediaId,
    string? ImageUrl,
    JsonElement? Tags,
    int SortOrder,
    bool IsAvailable);

public sealed class SaveMenuCategoryRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string CategoryName { get; init; } = string.Empty;

    [StringLength(255)]
    public string? CategoryDescription { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed class SaveMenuItemRequest
{
    [Required, StringLength(40)]
    public string CategoryId { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string ItemName { get; init; } = string.Empty;

    [StringLength(500)]
    public string? ItemDescription { get; init; }

    [Range(0, int.MaxValue)]
    public int Price { get; init; }

    [StringLength(40)]
    public string? MediaId { get; init; }

    [StringLength(500)]
    public string? ImageUrl { get; init; }

    public JsonElement? Tags { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsAvailable { get; init; } = true;
}

public sealed record AdminMenuSetItemDto(
    string Id,
    string MenuItemId,
    string ItemName,
    string ItemRole,
    int Quantity,
    int SortOrder);

public sealed record AdminMenuSetDto(
    string Id,
    string SetName,
    string? SetDescription,
    int SetPrice,
    string? MediaId,
    string? ImageUrl,
    int SortOrder,
    bool IsAvailable,
    IReadOnlyList<AdminMenuSetItemDto> Items);

public sealed class SaveMenuSetRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string SetName { get; init; } = string.Empty;

    [StringLength(500)]
    public string? SetDescription { get; init; }

    [Range(0, int.MaxValue)]
    public int SetPrice { get; init; }

    [StringLength(40)]
    public string? MediaId { get; init; }

    [StringLength(500)]
    public string? ImageUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsAvailable { get; init; } = true;
    public List<SaveMenuSetItemRequest> Items { get; init; } = [];
}

public sealed class SaveMenuSetItemRequest
{
    [StringLength(40)]
    public string? Id { get; init; }

    [Required, StringLength(40)]
    public string MenuItemId { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string ItemRole { get; init; } = "main";

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; } = 1;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public sealed record AdminMenuDto(
    IReadOnlyList<AdminPricingRuleDto> PricingRules,
    IReadOnlyList<AdminMenuCategoryDto> Categories,
    IReadOnlyList<AdminMenuSetDto> Sets);

public sealed record AdminMediaUploadDto(
    string Id,
    string Category,
    string FileName,
    string ContentType,
    string Url);

public sealed class CleanupMediaRequest
{
    public List<string> MediaIds { get; init; } = [];
}
