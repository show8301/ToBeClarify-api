using System.Security.Cryptography;
using Dapper;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Services.Media;

// Operator-invoked only. No automatic schema changes, startup migration, or blob deletion.
public sealed class PrivateImageMigration(AppDbContext db, MediaDiskStorage disk, ILogger<PrivateImageMigration> logger)
{
    public async Task Run(string mode, CancellationToken ct)
    {
        if (mode is not ("copy" or "verify" or "clear-legacy" or "report-orphans"))
            throw new ArgumentException("private-image-migration must be copy, verify, clear-legacy, or report-orphans.");
        _ = disk.PrivateRoot; // Validate configuration before touching any records.
        if (mode == "report-orphans") { await ReportOrphans(ct); return; }
        var processed = 0;
        foreach (var category in new[] { "guestbook", "delivery" })
        {
            var categoryCount = 0;
            long categoryBytes = 0;
            var table = category == "guestbook" ? "GUESTBOOK_IMAGES" : "ART_DELIVERY_ASSETS";
            var blob = category == "guestbook" ? "IMAGE_BYTES" : "FILE_BYTES";
            var predicate = category == "guestbook" ? "TRUE" : "KIND='image'";
            string cursor = "";
            while (true)
            {
                await using var c = await db.CreateOpenConnectionAsync(ct);
                var ids = (await c.QueryAsync<string>(new CommandDefinition(
                    $"SELECT ID FROM {table} WHERE {predicate} AND ID>@Cursor ORDER BY ID LIMIT 100;",
                    new { Cursor = cursor }, cancellationToken: ct))).AsList();
                if (ids.Count == 0) break;
                foreach (var id in ids)
                {
                    // Lock one row at a time; never keep all image blobs in memory.
                    await using var tx = await c.BeginTransactionAsync(ct);
                    var row = await c.QuerySingleOrDefaultAsync<PrivateImageRow>(new CommandDefinition(
                        $"SELECT STORAGE_PATH AS StoragePath,BYTE_SIZE AS ByteSize,SHA256 AS Sha256,{blob} AS LegacyBytes FROM {table} WHERE ID=@Id FOR UPDATE;",
                        new { Id = id }, tx, cancellationToken: ct));
                    if (row is null) { await tx.RollbackAsync(ct); continue; }
                    if (row.StoragePath is null)
                    {
                        if (mode != "copy") throw new InvalidDataException($"Unmigrated {category} image {id}; run copy first.");
                        var legacy = row.LegacyBytes ?? throw new InvalidDataException($"Image {id} has no bytes.");
                        var stored = await disk.WritePrivate(category, id, legacy, ct);
                        await c.ExecuteAsync(new CommandDefinition(
                            $"UPDATE {table} SET STORAGE_PATH=@StoragePath,BYTE_SIZE=@ByteSize,SHA256=@Sha256 WHERE ID=@Id;",
                            new { Id = id, stored.StoragePath, stored.ByteSize, stored.Sha256 }, tx, cancellationToken: ct));
                        row.StoragePath = stored.StoragePath;
                        row.ByteSize = stored.ByteSize;
                        row.Sha256 = stored.Sha256;
                    }
                    var bytes = await row.Read(disk, ct);
                    if (row.LegacyBytes is not null && !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(row.LegacyBytes)))
                        throw new InvalidDataException($"File/DB checksum mismatch for {category} image {id}; legacy bytes retained.");
                    if (mode == "clear-legacy" && row.LegacyBytes is not null)
                        await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {blob}=NULL WHERE ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
                    await tx.CommitAsync(ct);
                    processed++;
                    categoryCount++;
                    categoryBytes += bytes.LongLength;
                }
                cursor = ids[^1];
                logger.LogInformation("Private image migration {Mode}: {Category}, processed {Count} images.", mode, category, processed);
            }
            logger.LogInformation("Private image migration {Mode}: {Category} complete, {Count} images, {Bytes} bytes verified.", mode, category, categoryCount, categoryBytes);
        }
        logger.LogInformation("Private image migration {Mode} completed: {Count} images. Includes hidden/detached guestbook images and soft-removed delivery attachments.", mode, processed);
    }

    private async Task ReportOrphans(CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var paths = (await c.QueryAsync<string>(new CommandDefinition("""
            SELECT STORAGE_PATH FROM GUESTBOOK_IMAGES WHERE STORAGE_PATH IS NOT NULL
            UNION SELECT STORAGE_PATH FROM ART_DELIVERY_ASSETS WHERE STORAGE_PATH IS NOT NULL;
            """, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        // Report only: crashes/ambiguous commits can leave files. Never infer deletion
        // authority from age alone, especially while uploads or migration are active.
        if (Directory.Exists(disk.PrivateRoot))
            foreach (var file in Directory.EnumerateFiles(disk.PrivateRoot, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false
            }))
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.GetRelativePath(disk.PrivateRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!paths.Contains(path))
                {
                    logger.LogInformation("Unreferenced private file (report only): {RelativePath}", path);
                    count++;
                }
            }
        logger.LogInformation("Private image orphan report completed: {Count} files; no files deleted.", count);
    }
}
