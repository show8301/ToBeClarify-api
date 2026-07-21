using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Models.Media;

namespace ToBeClarify.Api.Services.Media;

public sealed class MediaUrlService
{
    private readonly MediaOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MediaUrlService(IOptions<MediaOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public string? BuildUrl(string? mediaId, string variant = "original")
        => BuildUrl(mediaId, null, variant);

    public string? BuildUrl(string? mediaId, string? legacyUrl, string variant = "original")
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return string.IsNullOrWhiteSpace(legacyUrl) ? null : legacyUrl;

        var path = $"{_options.RoutePrefix.TrimEnd('/')}/{Uri.EscapeDataString(mediaId)}";
        var url = QueryHelpers.AddQueryString(path, "variant", variant);
        var baseUrl = _options.PublicBaseUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl)) return $"{baseUrl}{url}";

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null) return url;
        return $"{request.Scheme}://{request.Host}{url}";
    }
}
