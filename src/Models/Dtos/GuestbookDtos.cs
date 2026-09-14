using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed class GuestbookMessage
{
    public string Id { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Content { get; set; } = "";
    public string AuthorType { get; set; } = "customer";
    public bool IsVisible { get; set; }
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
    public bool AllowReplies { get; set; }
    public int ReplyCount { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
}
public sealed record GuestbookList(int Page, int PageSize, int TotalCount,
    IReadOnlyList<GuestbookMessage> Items, IReadOnlyList<GuestbookMessage> PinnedItems, string? NextCursor = null);
public sealed record GuestbookReplies(int Page, int PageSize, int TotalCount, IReadOnlyList<GuestbookMessage> Items, string? NextCursor = null);
public sealed record GuestbookSettings(string MascotName, int Version);
public sealed class GuestbookWrite
{
    [StringLength(60)] public string DisplayName { get; init; } = "";
    [Required, StringLength(2000, MinimumLength = 1)] public string Content { get; init; } = "";
    [StringLength(24)] public string AuthorType { get; init; } = "staff";
    [StringLength(200)] public string Website { get; init; } = "";
}
public sealed class GuestbookEdit
{
    [Required, StringLength(60, MinimumLength = 1)] public string DisplayName { get; init; } = "";
    [Required, StringLength(2000, MinimumLength = 1)] public string Content { get; init; } = "";
    [Range(1, int.MaxValue)] public int Version { get; init; }
}
public sealed record GuestbookModerate(int Version, bool? IsVisible, bool? AllowReplies, bool? IsPinned);
public sealed record GuestbookPin(string Id, int Version);
public sealed record GuestbookPinOrder(IReadOnlyList<GuestbookPin> Items);
