namespace ToBeClarify.Api.Models.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string RootPath { get; set; } = "media";
    // Must be outside IIS/static-file roots. Never exposed by /api/client/media.
    public string PrivateRootPath { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string RoutePrefix { get; set; } = "/api/client/media";
}
