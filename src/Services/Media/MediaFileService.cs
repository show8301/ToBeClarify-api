using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Models.Media;
using ToBeClarify.Api.Repositories.Client.Media;

namespace ToBeClarify.Api.Services.Media;

public sealed record MediaFileResult(Stream Stream, string ContentType, string FileName, int Version);

public sealed class MediaFileService
{
    private static readonly IReadOnlyDictionary<string, Size> VariantSizes = new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase)
    {
        ["thumbnail"] = new Size(480, 480),
        ["card"] = new Size(960, 720),
        ["hero"] = new Size(1600, 1000),
        ["full"] = new Size(2048, 2048),
    };

    private readonly IMediaRepository _repository;
    private readonly string _rootPath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new(StringComparer.OrdinalIgnoreCase);

    public MediaFileService(
        IMediaRepository repository,
        IOptions<MediaOptions> options,
        IWebHostEnvironment environment)
    {
        _repository = repository;
        var configuredRoot = options.Value.RootPath?.Trim();
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException("Media:RootPath must be configured.");
        _rootPath = Path.GetFullPath(configuredRoot, environment.ContentRootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<MediaFileResult> OpenAsync(string id, string? variant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new BusinessException("Media id is required.", "MEDIA_ID_REQUIRED");

        var asset = await _repository.GetByIdAsync(id.Trim(), cancellationToken)
            ?? throw new NotFoundException("Media asset not found.", "MEDIA_NOT_FOUND");
        var normalizedVariant = NormalizeVariant(variant);
        var sourcePath = ResolveSafePath(asset.StoragePath);
        if (!File.Exists(sourcePath))
            throw new NotFoundException("Media file not found on disk.", "MEDIA_FILE_NOT_FOUND");

        if (normalizedVariant == "original")
        {
            return new MediaFileResult(File.OpenRead(sourcePath), asset.MimeType, Path.GetFileName(sourcePath), asset.Version);
        }

        var derivedPath = GetDerivedPath(sourcePath, normalizedVariant);
        if (!File.Exists(derivedPath))
        {
            var gate = _generationLocks.GetOrAdd(derivedPath, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(derivedPath))
                    await GenerateVariantAsync(sourcePath, derivedPath, normalizedVariant, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        return new MediaFileResult(File.OpenRead(derivedPath), "image/webp", Path.GetFileName(derivedPath), asset.Version);
    }

    private static string NormalizeVariant(string? variant)
    {
        var value = string.IsNullOrWhiteSpace(variant) ? "original" : variant.Trim().ToLowerInvariant();
        if (value != "original" && !VariantSizes.ContainsKey(value))
            throw new BusinessException("Unsupported media variant.", "MEDIA_VARIANT_INVALID");
        return value;
    }

    private string ResolveSafePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
            throw new BusinessException("Invalid media storage path.", "MEDIA_PATH_INVALID");

        var root = Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, storagePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Invalid media storage path.", "MEDIA_PATH_INVALID");
        return fullPath;
    }

    private static string GetDerivedPath(string sourcePath, string variant)
    {
        var directory = Path.GetDirectoryName(sourcePath)!;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, $"{stem}_{variant}.webp");
    }

    private static async Task GenerateVariantAsync(
        string sourcePath,
        string derivedPath,
        string variant,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
        using var image = await Image.LoadAsync(sourcePath, cancellationToken);
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = VariantSizes[variant],
            Mode = ResizeMode.Max,
        }));
        await image.SaveAsWebpAsync(derivedPath, new WebpEncoder { Quality = 82 }, cancellationToken);
    }
}
