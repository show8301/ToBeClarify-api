using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Services.Client.Guestbook;

internal static class GuestbookImage
{
    private const int MaxBytes = 2 * 1024 * 1024;

    public static async Task<byte[]> Decode(string encoded, CancellationToken ct)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw Invalid(); }
        if (bytes.Length is 0 or > MaxBytes)
            throw new BusinessException("留言圖片上限為 2 MB。", "GUESTBOOK_IMAGE_TOO_LARGE");
        try
        {
            await using var input = new MemoryStream(bytes, writable: false);
            var format = await Image.DetectFormatAsync(input, ct);
            if (format.Name.ToUpperInvariant() is not ("JPEG" or "PNG" or "WEBP")) throw Invalid();
            input.Position = 0;
            var info = await Image.IdentifyAsync(input, ct);
            if (info.Width > 6000 || info.Height > 6000 || (long)info.Width * info.Height > 12_000_000)
                throw new BusinessException("請將留言圖片縮小到 1,200 萬畫素以下。", "GUESTBOOK_IMAGE_DIMENSIONS");
            input.Position = 0;
            using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, input, ct);
            image.Mutate(x => x.AutoOrient());
            await using var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = 85, SkipMetadata = true }, ct);
            if (output.Length > MaxBytes)
                throw new BusinessException("處理後的圖片超過 2 MB，請縮小圖片。", "GUESTBOOK_IMAGE_TOO_LARGE");
            return output.ToArray();
        }
        catch (UnknownImageFormatException) { throw Invalid(); }
        catch (InvalidImageContentException) { throw Invalid(); }
    }

    private static BusinessException Invalid() => new("請選擇有效的 JPEG、PNG 或 WebP 圖片。", "GUESTBOOK_IMAGE_INVALID");
}
