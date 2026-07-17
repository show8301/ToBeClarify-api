using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Client.Shared;

internal static class ClientContentMappings
{
    internal static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);
    internal static readonly HashSet<string> NavigationPlacements = new(StringComparer.OrdinalIgnoreCase) { "navbar", "footer" };
    internal static readonly HashSet<string> RankingTypes = new(StringComparer.Ordinal) { "staffRanking", "monetaryRanking" };
    internal static readonly HashSet<string> EventStatuses = new(StringComparer.OrdinalIgnoreCase) { "scheduled", "active", "ended" };

    internal static SiteSettingDto MapSetting(SiteSettingRow row)
        => new(row.SettingKey, ParseJson(row.SettingValue) ?? ParseJson("{}")!.Value, row.Description);

    internal static NavigationItemDto BuildNavigation(
        NavigationItemRow row,
        IReadOnlyDictionary<string, NavigationItemRow[]> childrenByParent,
        HashSet<string> path)
    {
        if (!path.Add(row.Id))
            return new NavigationItemDto(row.Id, row.Label, row.RoutePath, row.Placement, row.IsDropdown, Array.Empty<NavigationItemDto>());

        var nextPath = new HashSet<string>(path, StringComparer.Ordinal);
        var children = childrenByParent.TryGetValue(row.Id, out var rows)
            ? rows.Select(child => BuildNavigation(child, childrenByParent, nextPath)).ToArray()
            : Array.Empty<NavigationItemDto>();
        return new NavigationItemDto(row.Id, row.Label, row.RoutePath, row.Placement, row.IsDropdown, children);
    }

    internal static PricingRuleDto MapPricingRule(PricingRuleRow row)
        => new(row.Id, row.Title, row.Description, row.PriceText);

    internal static StaffListItemDto MapStaffListItem(StaffRow row, IEnumerable<StaffServiceRow> services)
    {
        var serviceRows = services.ToArray();
        return new StaffListItemDto(row.Id, row.DisplayName, row.Nickname, row.AvatarUrl, row.RoleTitle,
            row.ShortBio, row.CurrentStatus, row.StatusText, row.TodayShift,
            serviceRows.Where(service => service.ServiceType == "common").Select(MapStaffService).ToArray(),
            serviceRows.Where(service => service.ServiceType == "special").Select(MapStaffService).ToArray());
    }

    internal static StaffServiceDto MapStaffService(StaffServiceRow row)
        => new(row.Id, row.ServiceType, row.ServiceName, row.ServiceDescription, row.PriceText);

    internal static EventDto MapEvent(EventRow row)
        => new(row.Id, row.Title, row.Summary, row.CoverImageUrl, ToTaiwanOffset(row.StartsAt), ToTaiwanOffset(row.EndsAt),
            row.Status, row.LocationText, ParseStringArray(row.DetailContent), row.NoticeContent);

    internal static GalleryAlbumDto MapGalleryAlbum(GalleryAlbumRow row)
        => new(row.Id, row.AlbumTitle, row.AlbumDescription, row.CoverImageUrl, row.PeriodText,
            row.EndsAt.HasValue ? ToTaiwanOffset(row.EndsAt.Value) : null);

    internal static GalleryItemDto MapGalleryItem(GalleryItemRow row)
        => new(row.Id, row.ImageUrl, row.ThumbnailUrl, row.Title, row.Caption,
            row.ShotAt.HasValue ? ToTaiwanOffset(row.ShotAt.Value) : null);

    internal static MenuItemDto MapMenuItem(MenuItemRow row)
        => new(row.Id, row.CategoryId, row.ItemName, row.ItemDescription, row.Price, row.ImageUrl, ParseJson(row.Tags));

    internal static MenuSetItemDto MapMenuSetItem(MenuSetItemRow row)
        => new(row.Id, row.MenuItemId, row.ItemName, row.ItemRole, row.Quantity);

    internal static IReadOnlyList<GuestbookCommentDto> MapGuestbookComments(
        IReadOnlyList<GuestbookCommentRow> comments,
        IReadOnlyList<GuestbookReplyRow> replies)
        => comments.Select(comment => new GuestbookCommentDto(comment.Id, comment.DisplayName, comment.Content,
            comment.IsPinned, ToTaiwanOffset(comment.CreatedAt), replies.Where(reply => reply.CommentId == comment.Id)
                .Select(reply => new GuestbookReplyDto(reply.Id, reply.DisplayName, reply.Content, ToTaiwanOffset(reply.CreatedAt)))
                .ToArray())).ToArray();

    internal static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return json.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    internal static DateTimeOffset ToTaiwanOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TaiwanOffset);

    internal static DateTime? ToTaiwanDateTime(DateTimeOffset? value)
        => value?.ToOffset(TaiwanOffset).DateTime;

    internal static string RequiredId(string value)
        => RequiredValue(value, "ID_REQUIRED", "ID is required.");

    internal static string RequiredValue(string value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new BusinessException(message, errorCode);
        return value.Trim();
    }

    internal static string CleanUserText(string value, int maxLength, string errorCode)
    {
        var cleaned = RequiredValue(value, errorCode, "Value is required.");
        if (cleaned.Length > maxLength || cleaned.Contains('\0') || cleaned.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Invalid text content.", errorCode);
        return cleaned;
    }

    internal static string? OptionalUserText(string? value, int maxLength, string errorCode)
        => string.IsNullOrWhiteSpace(value) ? null : CleanUserText(value, maxLength, errorCode);

    internal static string? HashUserToken(string? value)
        => value is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
