namespace ToBeClarify.Api.Services.Media;

// Internal persistence shape. Never serialize storage paths or legacy bytes into API DTOs.
public sealed class PrivateImageRow
{
    public string? StoragePath { get; set; }
    public long? ByteSize { get; set; }
    public string? Sha256 { get; set; }
    public byte[]? LegacyBytes { get; set; }
    public string? StorageKey { get; set; }
    public string? DeliveryId { get; set; }
    public long? LegacyLength { get; set; }
    public string? LegacySha256 { get; set; }

    public Task<byte[]> Read(MediaDiskStorage disk, CancellationToken ct)
    {
        if (StoragePath is null)
            return Task.FromResult(LegacyBytes ?? throw new InvalidDataException("Image has no file or legacy bytes."));
        if (ByteSize is null || Sha256 is null)
            throw new InvalidDataException("Incomplete private image metadata.");
        return disk.ReadPrivate(new StoredImage(StoragePath, ByteSize.Value, Sha256), ct);
    }
}
