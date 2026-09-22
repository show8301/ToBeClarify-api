using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Customers;

namespace ToBeClarify.Api.Services.Customers;

public sealed class ArtDeliveryService(CustomerDeliveryRepository repository, CustomerIdentityService identities)
{
    private static string ClaimCode() => "D-" + string.Join('-', Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).Chunk(8).Select(x => new string(x)));
    private static string ClaimHash(string code)
    {
        var cleaned = new string(code.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (cleaned.Length != 33 || cleaned[0] != 'D' || cleaned[1..].Any(c => !Uri.IsHexDigit(c)))
            throw new UnauthorizedException("領取碼無效。", "DELIVERY_CREDENTIAL_INVALID");
        return CustomerIdentityService.Hash(cleaned, "delivery");
    }
    private static string Required(string? value, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > max || text.Any(char.IsControl)) throw new BusinessException("欄位內容不正確。", "VALIDATION_ERROR");
        return text;
    }
    public Task<IReadOnlyList<ArtDeliveryDto>> List(string? sessionId, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || sessionId?.Length > 36 || search?.Length > 100 || (status is not null && status is not ("pending" or "in_progress" or "ready" or "delivered" or "cancelled")))
            throw new BusinessException("搜尋參數不正確。", "VALIDATION_ERROR");
        return repository.Deliveries(sessionId, status, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), null, null, false, ct, pageSize, (page - 1) * pageSize);
    }
    public Task<ArtDeliveryDto> Get(string id, CancellationToken ct) => repository.Delivery(id, ct);
    public async Task<ArtDeliveryIssuedDto> Create(CreateArtDeliveryRequest r, ClaimsPrincipal actor, CancellationToken ct)
    {
        _ = Required(r.Title, 160);
        var code = ClaimCode();
        var id = await repository.CreateDelivery(r, ClaimHash(code), CustomerIdentityService.ActorId(actor), ct);
        return new(await repository.Delivery(id, ct), code);
    }
    public async Task<ArtDeliveryDto> Update(string id, UpdateArtDeliveryRequest r, ClaimsPrincipal actor, CancellationToken ct)
    {
        _ = Required(r.Title, 160);
        await repository.UpdateDelivery(id, r, CustomerIdentityService.ActorId(actor), ct);
        return await repository.Delivery(id, ct);
    }
    public async Task<ArtDeliveryIssuedDto> Reissue(string id, ClaimsPrincipal actor, CancellationToken ct)
    {
        var code = ClaimCode();
        await repository.ReissueCode(id, ClaimHash(code), CustomerIdentityService.ActorId(actor), ct);
        return new(await repository.Delivery(id, ct), code);
    }
    public async Task<ArtDeliveryDto> AddLink(string id, AddDeliveryLinkRequest r, ClaimsPrincipal actor, CancellationToken ct)
    {
        var label = Required(r.Label, 160);
        var url = r.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Contains('.') || IPAddress.TryParse(uri.Host, out _) || url.Any(char.IsControl))
            throw new BusinessException("請使用有效的 HTTPS 雲端分享連結。", "DELIVERY_LINK_INVALID");
        // This URL is never fetched by the API. Browser users explicitly choose to open it.
        await repository.AddAsset(id, label, uri.AbsoluteUri, null, CustomerIdentityService.ActorId(actor), ct);
        return await repository.Delivery(id, ct);
    }
    public async Task<ArtDeliveryDto> Upload(string id, IFormFile file, string? label, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024)
            throw new BusinessException("圖片需為 10 MiB 以內。", "DELIVERY_IMAGE_SIZE");
        var title = Required(string.IsNullOrWhiteSpace(label) ? "作品圖片" : label, 160);
        byte[] bytes;
        try
        {
            await using var input = file.OpenReadStream();
            var options = new DecoderOptions { MaxFrames = 2, SkipMetadata = false };
            var info = await Image.IdentifyAsync(options, input, ct);
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 24_000_000 || info.FrameMetadataCollection.Count > 1
                || info.Metadata.DecodedImageFormat?.Name is not ("JPEG" or "PNG" or "Webp" or "WEBP"))
                throw new BusinessException("請上傳 2400 萬畫素以內的靜態 JPEG、PNG 或 WebP。", "DELIVERY_IMAGE_INVALID");
            input.Position = 0;
            using var image = await Image.LoadAsync(options, input, ct);
            if (image.Frames.Count != 1) throw new BusinessException("不支援動態圖片。", "DELIVERY_IMAGE_INVALID");
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            await using var output = new MemoryStream();
            await image.SaveAsync(output, new PngEncoder { SkipMetadata = true }, ct);
            if (output.Length > 12 * 1024 * 1024) throw new BusinessException("圖片無損處理後超過 12 MiB，請縮小圖片或使用雲端連結。", "DELIVERY_IMAGE_SIZE");
            bytes = output.ToArray();
        }
        catch (UnknownImageFormatException) { throw new BusinessException("圖片格式不支援。", "DELIVERY_IMAGE_INVALID"); }
        catch (InvalidImageContentException) { throw new BusinessException("圖片檔案無法讀取。", "DELIVERY_IMAGE_INVALID"); }
        await repository.AddAsset(id, title, null, bytes, CustomerIdentityService.ActorId(actor), ct);
        return await repository.Delivery(id, ct);
    }
    public async Task<ArtDeliveryDto> RemoveAsset(string id, string assetId, ClaimsPrincipal actor, CancellationToken ct)
    {
        await repository.RemoveAsset(id, assetId, CustomerIdentityService.ActorId(actor), ct);
        return await repository.Delivery(id, ct);
    }
    public Task<byte[]> AdminImage(string id, string assetId, CancellationToken ct) => repository.ImageBytes(id, assetId, null, null, false, ct);

    private async Task<(string? Uid, string? Hash)> Access(DeliveryAccessRequest r, CancellationToken ct)
    {
        // A single claim code grants one commission only. Supplying both mechanisms is rejected.
        if (!string.IsNullOrWhiteSpace(r.ClaimCode))
        {
            if (!string.IsNullOrWhiteSpace(r.CustomerUid))
                throw new BusinessException("請選擇一種領取方式。", "DELIVERY_ACCESS_AMBIGUOUS");
            return (null, ClaimHash(r.ClaimCode));
        }
        return ((await identities.ResolveUid(r.CustomerUid, ct)).Uid, null);
    }
    public async Task<PublicDeliveryListDto> Lookup(DeliveryAccessRequest r, CancellationToken ct)
    {
        var access = await Access(r, ct);
        var page = access.Hash is null ? r.Page : 1;
        var size = access.Hash is null ? r.PageSize : 1;
        var rows = await repository.Deliveries(null, null, null, access.Uid, access.Hash, true, ct, size + 1, (page - 1) * size);
        if (access.Hash is not null && rows.Count == 0) throw new UnauthorizedException("領取碼無效。", "DELIVERY_CREDENTIAL_INVALID");
        return new(rows.Take(size).Select(Public).ToArray(), page, size, rows.Count > size);
    }
    public async Task<PublicArtDeliveryDto> Acknowledge(string id, DeliveryAccessRequest r, CancellationToken ct)
    {
        var access = await Access(r, ct);
        await repository.Acknowledge(id, access.Uid, access.Hash, ct);
        var row = await repository.Delivery(id, ct);
        foreach (var asset in row.Assets.Where(x => x.Kind == "image")) asset.Url = $"/api/client/art-deliveries/{id}/assets/{asset.Id}";
        return Public(row);
    }
    public async Task<byte[]> ClientImage(string id, string assetId, DeliveryAccessRequest r, CancellationToken ct)
    {
        var access = await Access(r, ct);
        return await repository.ImageBytes(id, assetId, access.Uid, access.Hash, true, ct);
    }
    private static PublicArtDeliveryDto Public(ArtDeliveryDto x) => new(x.Id, x.Title, x.Description, x.Status, x.DueDate, x.CreatedAt, x.DeliveredAt,
        x.Status is "ready" or "delivered" ? x.Assets : []);
}
