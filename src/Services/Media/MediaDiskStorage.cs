using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Media;

namespace ToBeClarify.Api.Services.Media;

public sealed record StoredImage(string StoragePath, long ByteSize, string Sha256);

// Shared disk IO; authorization and database ownership remain with each feature.
public sealed class MediaDiskStorage(IOptions<MediaOptions> options, IWebHostEnvironment environment)
{
    private string PublicRoot => Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);

    public string PrivateRoot
    {
        get
        {
            var configured = options.Value.PrivateRootPath;
            if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("Configure Media:PrivateRootPath as an absolute, non-public, persistent directory before enabling private image storage.");
            var root = Path.GetFullPath(configured);
            if (Within(root, PublicRoot) || Within(PublicRoot, root) || Within(root, environment.ContentRootPath)
                || (!string.IsNullOrEmpty(environment.WebRootPath) && Within(root, environment.WebRootPath)))
                throw new InvalidOperationException("Media:PrivateRootPath must not overlap public media or be inside the application/static root.");
            return root;
        }
    }

    public Task WritePublic(string relativePath, Stream source, CancellationToken ct)
        => WriteAtomic(Resolve(PublicRoot, relativePath), source, ct);

    public async Task<StoredImage> WritePrivate(string category, string id, byte[] bytes, CancellationToken ct)
    {
        if (category is not ("guestbook" or "delivery") || !Guid.TryParse(id, out var guid) || bytes.Length == 0)
            throw new InvalidOperationException("Invalid private image identity.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = $"{category}/{guid:D}/{hash}.{(category == "guestbook" ? "webp" : "png")}";
        var fullPath = Resolve(PrivateRoot, path);
        if (!File.Exists(fullPath))
        {
            await using var source = new MemoryStream(bytes, writable: false);
            try { await WriteAtomic(fullPath, source, ct); }
            catch (IOException) when (File.Exists(fullPath)) { /* concurrent retry; verify below */ }
        }
        var result = new StoredImage(path, bytes.LongLength, hash);
        _ = await ReadPrivate(result, ct);
        return result;
    }

    public async Task<byte[]> ReadPrivate(StoredImage image, CancellationToken ct)
    {
        var path = Resolve(PrivateRoot, image.StoragePath);
        if (!File.Exists(path)) throw new NotFoundException("找不到圖片檔案。", "MEDIA_FILE_NOT_FOUND");
        // Check the opened handle, not FileInfo followed by an unbounded read.
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (image.ByteSize <= 0 || image.ByteSize > 12 * 1024 * 1024 || input.Length != image.ByteSize)
            throw new InvalidDataException("Private image length mismatch.");
        var bytes = new byte[checked((int)image.ByteSize)];
        await input.ReadExactlyAsync(bytes, ct);
        if (input.Length != image.ByteSize || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Private image checksum mismatch.");
        return bytes;
    }

    private static async Task WriteAtomic(string destination, Stream source, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool Within(string path, string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(x => x is ".." or "." or ""))
            throw new InvalidDataException("Invalid media storage path.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(path, root)) throw new InvalidDataException("Invalid media storage path.");
        // Storage directories are operator-owned; reject links to prevent root escape.
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
        {
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new InvalidDataException("Media paths must not contain symbolic links/reparse points.");
        }
        return path;
    }
}
