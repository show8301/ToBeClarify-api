using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Models.Media;

namespace ToBeClarify.Api.Services.Media;

public sealed class AdminMediaUploadService
{
    private static readonly IReadOnlyDictionary<string, string> SupportedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    private readonly AppDbContext _dbContext;
    private readonly MediaOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly MediaUrlService _mediaUrls;

    public AdminMediaUploadService(
        AppDbContext dbContext,
        IOptions<MediaOptions> options,
        IWebHostEnvironment environment,
        MediaUrlService mediaUrls)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _environment = environment;
        _mediaUrls = mediaUrls;
    }

    public async Task<AdminMediaUploadDto> UploadAsync(IFormFile file, string? category, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new BusinessException("Image file is required.", "MEDIA_FILE_REQUIRED");
        if (file.Length > 10 * 1024 * 1024)
            throw new BusinessException("Image file must be 10 MB or smaller.", "MEDIA_FILE_TOO_LARGE");
        if (!SupportedTypes.TryGetValue(file.ContentType, out var extension))
            throw new BusinessException("Only JPEG, PNG, WebP, and GIF images are supported.", "MEDIA_TYPE_NOT_SUPPORTED");

        var normalizedCategory = NormalizeCategory(category);
        if (actor.FindFirstValue(AdminAuthConstants.RoleClaimType) == AdminRole.Clerk &&
            normalizedCategory is not ("staff" or "gallery"))
            throw new ForbiddenException("Clerk accounts can only upload staff images.", "MEDIA_SCOPE_FORBIDDEN");
        var id = Guid.NewGuid().ToString("D");
        var relativeDirectory = normalizedCategory == "home"
            ? normalizedCategory
            : Path.Combine(normalizedCategory, DateTime.UtcNow.ToString("yyyyMM"));
        var rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.RootPath) ? "media" : _options.RootPath, _environment.ContentRootPath);
        var directory = Path.Combine(rootPath, relativeDirectory);
        Directory.CreateDirectory(directory);
        var relativePath = Path.Combine(relativeDirectory, $"{id}{extension}").Replace(Path.DirectorySeparatorChar, '/');
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        int? width = null;
        int? height = null;
        await using (var input = file.OpenReadStream())
        {
            var imageInfo = await Image.IdentifyAsync(input, cancellationToken);
            if (imageInfo is null)
                throw new BusinessException("The uploaded file is not a valid image.", "MEDIA_IMAGE_INVALID");
            width = imageInfo.Width;
            height = imageInfo.Height;
        }

        await using (var output = File.Create(fullPath))
        await file.CopyToAsync(output, cancellationToken);

        try
        {
            const string sql = """
                INSERT INTO MEDIA_ASSETS
                    (ID, CATEGORY, STORAGE_PATH, MIME_TYPE, ORIGINAL_FILE_NAME, FILE_SIZE, WIDTH, HEIGHT, VERSION, IS_ACTIVE, CREATED_AT, CREATED_BY, UPDATED_AT, UPDATED_BY)
                VALUES (@Id, @Category, @StoragePath, @MimeType, @OriginalFileName, @FileSize, @Width, @Height, 1, TRUE, CURRENT_TIMESTAMP, @ActorId, CURRENT_TIMESTAMP, @ActorId);
                """;
            await using var connection = await _dbContext.CreateOpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = id,
                Category = normalizedCategory,
                StoragePath = relativePath,
                MimeType = file.ContentType,
                OriginalFileName = Path.GetFileName(file.FileName),
                FileSize = file.Length,
                Width = width,
                Height = height,
                ActorId = ActorId(actor),
            }, cancellationToken: cancellationToken));
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }

        return new AdminMediaUploadDto(id, normalizedCategory, Path.GetFileName(file.FileName), file.ContentType,
            _mediaUrls.BuildUrl(id, null, "original") ?? string.Empty);
    }

    public async Task DeleteHomeAssetAsync(string? mediaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return;

        await using var connection = await _dbContext.CreateOpenConnectionAsync(cancellationToken);
        var asset = await connection.QuerySingleOrDefaultAsync<MediaAssetRow>(new CommandDefinition("""
            SELECT `STORAGE_PATH` AS StoragePath, `CATEGORY` AS Category
            FROM `MEDIA_ASSETS` WHERE `ID` = @Id LIMIT 1;
            """, new { Id = mediaId }, cancellationToken: cancellationToken));
        if (asset is null || string.IsNullOrWhiteSpace(asset.StoragePath) || !string.Equals(asset.Category, "home", StringComparison.OrdinalIgnoreCase))
            return;

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MEDIA_ASSETS` WHERE `ID` = @Id AND `CATEGORY` = 'home';",
            new { Id = mediaId }, cancellationToken: cancellationToken));

        var rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.RootPath) ? "media" : _options.RootPath, _environment.ContentRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, asset.StoragePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(fullPath)) File.Delete(fullPath);
        foreach (var suffix in new[] { "_thumbnail.webp", "_card.webp", "_hero.webp", "_full.webp" })
        {
            var derivedPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $"{Path.GetFileNameWithoutExtension(fullPath)}{suffix}");
            if (File.Exists(derivedPath)) File.Delete(derivedPath);
        }
    }

    private static string NormalizeCategory(string? category)
    {
        var value = string.IsNullOrWhiteSpace(category) ? "admin" : category.Trim().ToLowerInvariant();
        return value is "site" or "home" or "staff" or "event" or "menu" or "gallery" or "admin"
            ? value
            : throw new BusinessException("Invalid media category.", "MEDIA_CATEGORY_INVALID");
    }

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();
}
