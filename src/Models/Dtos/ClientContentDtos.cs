using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record SiteSettingDto(string SettingKey, JsonElement SettingValue, string? Description);

public sealed record NavigationItemDto(
    string Id,
    string Label,
    string RoutePath,
    string Placement,
    bool IsDropdown,
    IReadOnlyList<NavigationItemDto> Children);

public sealed record HomeEventCarouselDto(
    string Id,
    string? EventId,
    string Title,
    string Summary,
    string? EventTime,
    string? CtaLabel,
    string? ImageUrl,
    bool EventExists);

public sealed record ShopRuleDto(string Id, string RuleText, string? RuleNote);

public sealed record PricingRuleDto(string Id, string Title, string Description, string? PriceText);

public sealed record StaffListItemDto(
    string Id,
    string DisplayName,
    string? Nickname,
    string? AvatarUrl,
    string? RoleTitle,
    string? ShortBio,
    string CurrentStatus,
    string? StatusText,
    string? TodayShift,
    IReadOnlyList<StaffServiceDto> CommonServices,
    IReadOnlyList<StaffServiceDto> SpecialServices);

public sealed record StaffServiceDto(
    string Id,
    string ServiceType,
    string ServiceName,
    string ServiceDescription,
    string? PriceText);

public sealed record StaffGalleryItemDto(string Id, string ImageUrl);

public sealed record StaffDetailDto(
    string Id,
    string DisplayName,
    string? Nickname,
    string? AvatarUrl,
    string? RoleTitle,
    string? ShortBio,
    string? ProfileBio,
    string CurrentStatus,
    string? StatusText,
    string? TodayShift,
    IReadOnlyList<StaffGalleryItemDto> Gallery,
    IReadOnlyList<StaffServiceDto> CommonServices,
    IReadOnlyList<StaffServiceDto> SpecialServices);

public sealed record EventDto(
    string Id,
    string Title,
    string Summary,
    string? CoverImageUrl,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string? LocationText,
    IReadOnlyList<string> Details,
    string? NoticeContent);

public sealed record GalleryAlbumDto(
    string Id,
    string AlbumTitle,
    string? AlbumDescription,
    string? CoverImageUrl,
    string? PeriodText,
    DateTimeOffset? EndsAt);

public sealed record GalleryItemDto(
    string Id,
    string ImageUrl,
    string? ThumbnailUrl,
    string? Title,
    string? Caption,
    DateTimeOffset? ShotAt);

public sealed record GalleryAlbumDetailDto(
    string Id,
    string AlbumTitle,
    string? AlbumDescription,
    string? CoverImageUrl,
    string? PeriodText,
    DateTimeOffset? EndsAt,
    IReadOnlyList<string> Details,
    IReadOnlyList<GalleryItemDto> Items);

public sealed record GuestbookReplyDto(
    string Id,
    string DisplayName,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record GuestbookCommentDto(
    string Id,
    string DisplayName,
    string Content,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GuestbookReplyDto> Replies);

public sealed record GuestbookPageDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<GuestbookCommentDto> Items);

public sealed class CreateGuestbookCommentRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(5000, MinimumLength = 1)]
    public string Content { get; init; } = string.Empty;

    [StringLength(120)]
    public string? UserToken { get; init; }
}

public sealed class CreateGuestbookReplyRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(5000, MinimumLength = 1)]
    public string Content { get; init; } = string.Empty;

    [StringLength(120)]
    public string? UserToken { get; init; }
}

public sealed record MenuItemDto(
    string Id,
    string CategoryId,
    string ItemName,
    string? ItemDescription,
    int Price,
    string? ImageUrl,
    JsonElement? Tags);

public sealed record MenuCategoryDto(
    string Id,
    string CategoryName,
    string? CategoryDescription,
    IReadOnlyList<MenuItemDto> Items);

public sealed record MenuSetItemDto(
    string Id,
    string MenuItemId,
    string ItemName,
    string ItemRole,
    int Quantity);

public sealed record MenuSetDto(
    string Id,
    string SetName,
    string? SetDescription,
    int SetPrice,
    string? ImageUrl,
    IReadOnlyList<MenuSetItemDto> Items);

public sealed record MenuDto(
    IReadOnlyList<PricingRuleDto> PricingRules,
    IReadOnlyList<MenuCategoryDto> Categories,
    IReadOnlyList<MenuSetDto> Sets);

public sealed record StaffReservationDto(
    string Id,
    string StaffId,
    string StaffName,
    string? StaffAvatar,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? ServiceLabel);

public sealed record RankingDto(
    string Id,
    string RankingType,
    string? TargetId,
    string DisplayName,
    string? Avatar,
    string? TitleBadge,
    int RankPosition,
    int ScoreValue,
    string? ScoreLabel,
    string? PeriodLabel);

public sealed record HomeDto(
    IReadOnlyList<SiteSettingDto> SiteSettings,
    IReadOnlyList<NavigationItemDto> Navigation,
    IReadOnlyList<HomeEventCarouselDto> Carousels,
    IReadOnlyList<ShopRuleDto> ShopRules,
    IReadOnlyList<StaffListItemDto> Staff,
    IReadOnlyList<EventDto> Events);
