namespace ToBeClarify.Api.Models.Entities;

public sealed class MediaAssetRow
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public string? OriginalFileName { get; set; }
    public long? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
