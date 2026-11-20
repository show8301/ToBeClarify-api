using System.Security.Cryptography;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Services.Media;

// Operator-invoked only. No automatic schema changes, startup migration, or blob deletion.
public sealed class PrivateImageMigration(AppDbContext db, MediaDiskStorage disk, ILogger<PrivateImageMigration> logger)
{
    internal const int ChunkSize = 1024 * 1024;

    public async Task Run(string mode, CancellationToken ct, string categoryFilter = "all")
    {
        if (mode is not ("copy" or "verify" or "clear-legacy" or "restore-legacy" or "verify-legacy" or "report-orphans"))
            throw new ArgumentException("Invalid private-image-migration mode.");
        if (categoryFilter is not ("all" or "delivery" or "guestbook"))
            throw new ArgumentException("private-image-category must be all, delivery, or guestbook.");
        _ = disk.PrivateRoot; // Validate configuration before touching any records.
        if (mode == "report-orphans") { await ReportOrphans(ct); return; }
        var processed = 0;
        foreach (var category in new[] { "guestbook", "delivery" }.Where(x => categoryFilter == "all" || x == categoryFilter))
        {
            var categoryCount = 0;
            long categoryBytes = 0;
            var table = category == "guestbook" ? "GUESTBOOK_IMAGES" : "ART_DELIVERY_ASSETS";
            var blob = category == "guestbook" ? "IMAGE_BYTES" : "FILE_BYTES";
            var predicate = category == "guestbook" ? "TRUE" : "KIND='image'";
            var storage = category == "guestbook" ? "STORAGE_PATH" : "COALESCE(STORAGE_KEY,STORAGE_PATH)";
            var extra = category == "guestbook" ? "NULL AS StorageKey,NULL AS DeliveryId" : "STORAGE_KEY AS StorageKey,DELIVERY_ID AS DeliveryId";
            await using var inventoryConnection = await db.CreateOpenConnectionAsync(ct);
            var before = await Totals(inventoryConnection, table, blob, predicate, ct);
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
                        $"SELECT {storage} AS StoragePath,BYTE_SIZE AS ByteSize,SHA256 AS Sha256,{extra},OCTET_LENGTH({blob}) AS LegacyLength,SHA2({blob},256) AS LegacySha256 FROM {table} WHERE ID=@Id FOR UPDATE;",
                        new { Id = id }, tx, cancellationToken: ct));
                    if (row is null) throw new InvalidDataException("Image inventory changed; pause writers and rerun.");
                    if (row.LegacyLength is not null)
                    {
                        row.LegacyBytes = await ReadLegacy(c, tx, table, blob, id, row.LegacyLength.Value, ct);
                        CheckHash(row.LegacyBytes, row.LegacySha256);
                        if (row.ByteSize is not null && row.ByteSize != row.LegacyLength)
                            throw new InvalidDataException("Legacy image byte count differs from metadata.");
                    }
                    var needsCopy = category == "delivery" ? row.StorageKey is null : row.StoragePath is null;
                    if (needsCopy && mode == "copy")
                    {
                        var legacy = row.StoragePath is null
                            ? row.LegacyBytes ?? throw new InvalidDataException($"Image {id} has no bytes.")
                            : await row.Read(disk, ct);
                        if (row.LegacySha256 is not null) CheckHash(legacy, row.LegacySha256);
                        var stored = await disk.WritePrivate(category, id, legacy, ct, row.DeliveryId);
                        var column = category == "delivery" ? "STORAGE_KEY" : "STORAGE_PATH";
                        await c.ExecuteAsync(new CommandDefinition(
                            $"UPDATE {table} SET {column}=@StoragePath,BYTE_SIZE=@ByteSize,SHA256=@Sha256 WHERE ID=@Id;",
                            new { Id = id, stored.StoragePath, stored.ByteSize, stored.Sha256 }, tx, cancellationToken: ct));
                        row.StoragePath = stored.StoragePath;
                        row.ByteSize = stored.ByteSize;
                        row.Sha256 = stored.Sha256;
                    }
                    else if (needsCopy && mode is not ("restore-legacy" or "verify-legacy"))
                        throw new InvalidDataException($"Unmigrated {category} image {id}; run copy first.");
                    var bytes = await row.Read(disk, ct);
                    if (mode == "verify-legacy" && row.LegacyLength is null)
                        throw new InvalidDataException("File-only row remains; old API rollback is unsafe.");
                    if (row.LegacyBytes is not null && !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(row.LegacyBytes)))
                        throw new InvalidDataException($"File/DB checksum mismatch for {category} image {id}; legacy bytes retained.");
                    if (mode == "clear-legacy" && row.LegacyBytes is not null)
                        await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {blob}=NULL WHERE ID=@Id;", new { Id = id }, tx, cancellationToken: ct));
                    if (mode == "restore-legacy" && row.LegacyLength is null)
                        await RestoreLegacy(c, tx, table, blob, id, bytes, ct);
                    await tx.CommitAsync(ct);
                    processed++;
                    categoryCount++;
                    categoryBytes += bytes.LongLength;
                }
                cursor = ids[^1];
                logger.LogInformation("Private image migration {Mode}: {Category}, processed {Count} images.", mode, category, processed);
            }
            var after = await Totals(inventoryConnection, table, blob, predicate, ct);
            if (before.Count != after.Count || before.Bytes != after.Bytes || categoryCount != after.Count || categoryBytes != after.Bytes)
                throw new InvalidDataException("Inventory totals changed or differ from files; pause writers and rerun verification.");
            logger.LogInformation("Private image migration {Mode}: {Category} complete, {Count} images, {Bytes} bytes verified.", mode, category, categoryCount, categoryBytes);
        }
        logger.LogInformation("Private image migration {Mode} completed: {Count} images. Includes hidden/detached guestbook images and soft-removed delivery attachments.", mode, processed);
    }

    private sealed class Inventory
    {
        public long Count { get; set; }
        public long Bytes { get; set; }
    }

    private static Task<Inventory> Totals(MySqlConnection c, string table, string blob, string predicate, CancellationToken ct)
        => c.QuerySingleAsync<Inventory>(new CommandDefinition(
            $"SELECT COUNT(*) AS Count,COALESCE(SUM(COALESCE(BYTE_SIZE,OCTET_LENGTH({blob}))),0) AS Bytes FROM {table} WHERE {predicate};", cancellationToken: ct));

    internal static async Task<byte[]> ReadLegacy(MySqlConnection c, MySqlTransaction tx, string table, string blob, string id, long length, CancellationToken ct)
    {
        // SQL identifiers come exclusively from Run's category allowlist.
        return await ReadChunks(length, async (offset, count) => await c.QuerySingleAsync<byte[]>(new CommandDefinition(
            $"SELECT SUBSTRING({blob},@Start,@Count) FROM {table} WHERE ID=@Id;",
            new { Id = id, Start = offset + 1, Count = count }, tx, cancellationToken: ct)), ct);
    }

    internal static async Task<byte[]> ReadChunks(long length, Func<int, int, Task<byte[]>> read, CancellationToken ct)
    {
        if (length is <= 0 or > 12 * 1024 * 1024) throw new InvalidDataException("Legacy image length outside supported limits.");
        var bytes = new byte[checked((int)length)];
        for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, bytes.Length - offset);
            ct.ThrowIfCancellationRequested();
            var chunk = await read(offset, count);
            if (chunk.Length != count) throw new InvalidDataException("Incomplete legacy image chunk.");
            chunk.CopyTo(bytes, offset);
        }
        return bytes;
    }

    private static void CheckHash(byte[] bytes, string? expected)
    {
        if (expected is null || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Image checksum mismatch; bytes retained.");
    }

    private static async Task RestoreLegacy(MySqlConnection c, MySqlTransaction tx, string table, string blob, string id, byte[] bytes, CancellationToken ct)
    {
        // No pre-existing bytes are overwritten. The row lock and transaction span all chunks.
        await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {blob}=X'' WHERE ID=@Id AND {blob} IS NULL;", new { Id = id }, tx, cancellationToken: ct));
        for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(ChunkSize, bytes.Length - offset)).ToArray();
            await c.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {blob}=CONCAT({blob},@Chunk) WHERE ID=@Id;",
                new { Id = id, Chunk = chunk }, tx, cancellationToken: ct));
        }
        var restored = await c.QuerySingleAsync<PrivateImageRow>(new CommandDefinition(
            $"SELECT OCTET_LENGTH({blob}) AS LegacyLength,SHA2({blob},256) AS LegacySha256 FROM {table} WHERE ID=@Id;",
            new { Id = id }, tx, cancellationToken: ct));
        if (restored.LegacyLength != bytes.LongLength) throw new InvalidDataException("Restored blob length mismatch.");
        CheckHash(bytes, restored.LegacySha256);
    }

    private async Task ReportOrphans(CancellationToken ct)
    {
        await using var c = await db.CreateOpenConnectionAsync(ct);
        var paths = (await c.QueryAsync<string>(new CommandDefinition("""
            SELECT STORAGE_PATH FROM GUESTBOOK_IMAGES WHERE STORAGE_PATH IS NOT NULL
            UNION SELECT STORAGE_PATH FROM ART_DELIVERY_ASSETS WHERE STORAGE_PATH IS NOT NULL
            UNION SELECT STORAGE_KEY FROM ART_DELIVERY_ASSETS WHERE STORAGE_KEY IS NOT NULL;
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
