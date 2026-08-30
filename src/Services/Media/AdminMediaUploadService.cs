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

// Database account policy: do not execute DELETE for media rows. Keep media metadata
// for audit/reporting and use an update-based inactive state; file cleanup also requires
// an explicit retention/DBA policy before it can be enabled.
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
    private readonly ILogger<AdminMediaUploadService> _logger;

    public AdminMediaUploadService(
        AppDbContext dbContext,
        IOptions<MediaOptions> options,
        IWebHostEnvironment environment,
        MediaUrlService mediaUrls,
        ILogger<AdminMediaUploadService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _environment = environment;
        _mediaUrls = mediaUrls;
        _logger = logger;
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
        var relativeDirectory = normalizedCategory;
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

    public async Task<int> CleanupUnreferencedAsync(
        IReadOnlyCollection<string>? mediaIds,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        var ids = (mediaIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        if (ids.Length == 0 && actor.FindFirstValue(AdminAuthConstants.RoleClaimType) == AdminRole.Clerk)
            return 0;

        return await CleanupUnreferencedCoreAsync(ids, cancellationToken);
    }

    public Task DeleteHomeAssetAsync(string? mediaId, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(mediaId)
            ? Task.CompletedTask
            : CleanupUnreferencedCoreAsync([mediaId], cancellationToken);

    public Task<int> CleanupExpiredUnreferencedAsync(CancellationToken cancellationToken)
        => CleanupUnreferencedCoreAsync([], cancellationToken);

    public async Task<int> NormalizeMonthlyPathsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dbContext.CreateOpenConnectionAsync(cancellationToken);
        var assets = (await connection.QueryAsync<MediaAssetRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `CATEGORY` AS Category, `STORAGE_PATH` AS StoragePath
            FROM `MEDIA_ASSETS`
            WHERE `STORAGE_PATH` REGEXP '^[^/]+/[0-9]{6}/[^/]+$';
            """, cancellationToken: cancellationToken))).ToArray();

        var movedCount = 0;
        foreach (var asset in assets)
        {
            var fileName = Path.GetFileName(asset.StoragePath);
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(asset.Category)) continue;

            var targetPath = $"{asset.Category.Trim().ToLowerInvariant()}/{fileName}";
            if (string.Equals(asset.StoragePath, targetPath, StringComparison.OrdinalIgnoreCase)) continue;

            var conflictId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT `ID`
                FROM `MEDIA_ASSETS`
                WHERE `STORAGE_PATH` = @TargetPath AND `ID` <> @Id
                LIMIT 1;
                """, new { TargetPath = targetPath, asset.Id }, cancellationToken: cancellationToken));
            if (!string.IsNullOrWhiteSpace(conflictId))
            {
                _logger.LogWarning("Skipped media path migration for {MediaId}; target path {TargetPath} is already used by {ConflictId}.", asset.Id, targetPath, conflictId);
                continue;
            }

            var sourceFullPath = ResolveSafePath(asset.StoragePath);
            var targetFullPath = ResolveSafePath(targetPath);
            if (!File.Exists(sourceFullPath))
            {
                _logger.LogWarning("Skipped media path migration for {MediaId}; source file {SourcePath} does not exist.", asset.Id, asset.StoragePath);
                continue;
            }

            var movedFiles = new List<(string Source, string Target)>();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
                MoveFile(sourceFullPath, targetFullPath, movedFiles);
                foreach (var suffix in DerivedSuffixes)
                {
                    var sourceVariant = AddSuffix(sourceFullPath, suffix);
                    var targetVariant = AddSuffix(targetFullPath, suffix);
                    if (File.Exists(sourceVariant)) MoveFile(sourceVariant, targetVariant, movedFiles);
                }

                var affected = await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `MEDIA_ASSETS`
                    SET `STORAGE_PATH` = @TargetPath, `UPDATED_AT` = CURRENT_TIMESTAMP
                    WHERE `ID` = @Id AND `STORAGE_PATH` = @SourcePath;
                    """, new { TargetPath = targetPath, SourcePath = asset.StoragePath, asset.Id }, cancellationToken: cancellationToken));
                if (affected == 0) throw new InvalidOperationException("Media storage path changed before migration completed.");
                movedCount++;
            }
            catch (Exception ex)
            {
                RevertMovedFiles(movedFiles);
                _logger.LogWarning(ex, "Failed to migrate media asset {MediaId} from {SourcePath} to {TargetPath}.", asset.Id, asset.StoragePath, targetPath);
            }
        }

        return movedCount;
    }

    private async Task<int> CleanupUnreferencedCoreAsync(IReadOnlyCollection<string> mediaIds, CancellationToken cancellationToken)
    {
        await using var connection = await _dbContext.CreateOpenConnectionAsync(cancellationToken);
        var filter = mediaIds.Count > 0
            ? "M.`ID` IN @Ids AND M.`CREATED_BY` IS NOT NULL"
            : "M.`CREATED_BY` IS NOT NULL AND M.`CREATED_AT` < CURRENT_TIMESTAMP - INTERVAL 24 HOUR";
        var query = $"""
            SELECT M.`ID` AS Id, M.`CATEGORY` AS Category, M.`STORAGE_PATH` AS StoragePath
            FROM `MEDIA_ASSETS` M
            WHERE {filter}
            {ReferencePredicate("M")};
            """;
        var parameters = mediaIds.Count > 0 ? new { Ids = mediaIds } : null;
        var candidates = (await connection.QueryAsync<MediaAssetRow>(new CommandDefinition(query, parameters, cancellationToken: cancellationToken))).ToArray();
        if (candidates.Length == 0) return 0;

        var deleted = new List<MediaAssetRow>();
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            foreach (var asset in candidates)
            {
                var affected = await connection.ExecuteAsync(new CommandDefinition($"""
                    DELETE FROM `MEDIA_ASSETS`
                    WHERE `ID` = @Id AND `CREATED_BY` IS NOT NULL
                    {ReferencePredicate("`MEDIA_ASSETS`")};
                    """, new { asset.Id }, transaction, cancellationToken: cancellationToken));
                if (affected > 0) deleted.Add(asset);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var asset in deleted) DeleteAssetFiles(asset.StoragePath);
        return deleted.Count;
    }

    private void DeleteAssetFiles(string storagePath)
    {
        var fullPath = ResolveSafePath(storagePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        foreach (var suffix in DerivedSuffixes)
        {
            var derivedPath = AddSuffix(fullPath, suffix);
            if (File.Exists(derivedPath)) File.Delete(derivedPath);
        }
    }

    private string ResolveSafePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
            throw new BusinessException("Invalid media storage path.", "MEDIA_PATH_INVALID");

        var rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.RootPath) ? "media" : _options.RootPath, _environment.ContentRootPath);
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Invalid media storage path.", "MEDIA_PATH_INVALID");
        return fullPath;
    }

    private static string ReferencePredicate(string alias) => $"""
        AND NOT EXISTS (SELECT 1 FROM `SITE_SETTINGS` S WHERE JSON_SEARCH(S.`SETTING_VALUE`, 'one', {alias}.`ID`) IS NOT NULL)
        AND NOT EXISTS (SELECT 1 FROM `HOME_SLIDES` H WHERE H.`MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `HOME_EVENT_CAROUSELS` C WHERE C.`OVERRIDE_MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `STAFF_MEMBERS` SM WHERE SM.`AVATAR_MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `STAFF_GALLERY_ITEMS` SG WHERE SG.`MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `EVENTS` E WHERE E.`COVER_MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `GALLERY_ALBUMS` GA WHERE GA.`COVER_MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `GALLERY_ITEMS` GI WHERE GI.`MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `MENU_ITEMS` MI WHERE MI.`MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `MENU_SETS` MS WHERE MS.`MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` SR WHERE SR.`STAFF_AVATAR_MEDIA_ID` = {alias}.`ID`)
        AND NOT EXISTS (SELECT 1 FROM `RANKINGS` R WHERE R.`AVATAR_MEDIA_ID` = {alias}.`ID`)
        """;

    private static void MoveFile(string sourcePath, string targetPath, ICollection<(string Source, string Target)> movedFiles)
    {
        if (File.Exists(targetPath)) throw new IOException($"Target media file already exists: {targetPath}");
        File.Move(sourcePath, targetPath);
        movedFiles.Add((sourcePath, targetPath));
    }

    private static void RevertMovedFiles(IEnumerable<(string Source, string Target)> movedFiles)
    {
        foreach (var (source, target) in movedFiles.Reverse())
        {
            try
            {
                if (File.Exists(target) && !File.Exists(source)) File.Move(target, source);
            }
            catch
            {
                // Keep the original exception as the actionable failure; the next maintenance pass can retry.
            }
        }
    }

    private static string AddSuffix(string path, string suffix)
        => Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}{suffix}");

    private static readonly string[] DerivedSuffixes = ["_thumbnail.webp", "_card.webp", "_hero.webp", "_full.webp"];

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
