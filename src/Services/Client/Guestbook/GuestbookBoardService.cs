using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Shared;
using ToBeClarify.Api.Services.Customers;

namespace ToBeClarify.Api.Services.Client.Guestbook;

public sealed class GuestbookBoardService(GuestbookStore store, IConfiguration config, IHttpContextAccessor context, CustomerIdentityService identities)
{
    public static void Page(int page, int size)
    {
        if (page < 1 || page > 100000 || size is < 1 or > 50) throw new BusinessException("分頁參數不正確。", "INVALID_PAGE");
    }
    public static string Text(string? value, int max)
    {
        var text = (value ?? "").Trim().Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length == 0 || text.Length > max || text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
            throw new BusinessException($"請輸入 1–{max} 字的有效文字。", "INVALID_TEXT");
        return text;
    }
    public async Task<GuestbookList> List(int page, int size, CancellationToken ct, string? cursor = null)
    {
        Page(page, size);
        return Public(await store.List(page, size, false, "all", ct, cursor));
    }
    public async Task<GuestbookReplies> Replies(string id, int page, int size, CancellationToken ct, string? cursor = null)
    {
        Page(page, size);
        return Public(await store.Replies(id, page, size, false, ct, cursor));
    }
    public async Task<GuestbookMessage> Get(string id, CancellationToken ct)
        => Public(await store.Get(id, false, false, ct));
    public async Task<GuestbookMessage> Create(string? thread, GuestbookWrite write, CancellationToken ct)
    {
        var name = Text(string.IsNullOrWhiteSpace(write.DisplayName) ? "匿名旅人" : write.DisplayName, 60);
        var content = Text(write.Content, 2000);
        var key = VisitorKey();
        // Return a plausible receipt without storing honeypot submissions.
        if (!string.IsNullOrEmpty(write.Website)) return new GuestbookMessage { Id = Guid.NewGuid().ToString(), DisplayName = name, Content = content, ThreadId = thread ?? "", IsVisible = true, AllowReplies = true, CreatedAt = DateTime.UtcNow.AddHours(8) };
        CustomerProfileDto? profile = null;
        if (!string.IsNullOrWhiteSpace(write.CustomerUid))
            profile = await identities.ResolveUid(write.CustomerUid, ct);
        byte[]? image = null;
        if (write.ImageBase64 is not null)
        {
            if (profile is null) throw new ForbiddenException("圖片留言需要顧客 UID。", "CUSTOMER_UID_REQUIRED");
            image = await GuestbookImage.Decode(write.ImageBase64, ct);
        }
        return Public(await store.Create(thread, name, content, "customer", null, null, key, ct, profile?.Uid, image));
    }

    private static GuestbookList Public(GuestbookList value)
    {
        foreach (var item in value.Items) item.CustomerUid = null;
        foreach (var item in value.PinnedItems) item.CustomerUid = null;
        return value;
    }

    private static GuestbookReplies Public(GuestbookReplies value)
    {
        foreach (var item in value.Items) item.CustomerUid = null;
        return value;
    }

    private static GuestbookMessage Public(GuestbookMessage value)
    {
        value.CustomerUid = null;
        return value;
    }
    private string VisitorKey()
    {
        var http = context.HttpContext ?? throw new InvalidOperationException();
        var secret = config["Guestbook:ProxySecret"] ?? "";
        if (secret.Length < 32) throw new GuestbookUnavailableException();
        var ipText = http.Request.Headers["X-Guestbook-IP"].ToString();
        var timestamp = http.Request.Headers["X-Guestbook-Time"].ToString();
        var signature = http.Request.Headers["X-Guestbook-Signature"].ToString();
        if (!IPAddress.TryParse(ipText, out var ip) || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var time) || Math.Abs((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - time) > 60)
            throw new ForbiddenException("請由網站留言板送出留言。", "GUESTBOOK_PROXY_REQUIRED");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}\n{ipText}"));
        byte[] received;
        try { received = Convert.FromHexString(signature); } catch (FormatException) { throw new ForbiddenException("留言來源驗證失敗。", "GUESTBOOK_PROXY_REQUIRED"); }
        if (!CryptographicOperations.FixedTimeEquals(expected, received)) throw new ForbiddenException("留言來源驗證失敗。", "GUESTBOOK_PROXY_REQUIRED");
        var normalized = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("visitor:" + normalized)));
    }
}
