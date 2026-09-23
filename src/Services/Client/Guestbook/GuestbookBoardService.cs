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
        return Public(await store.List(page, size, false, "all", ct, cursor, ViewerKey()));
    }
    public async Task<GuestbookReplies> Replies(string id, int page, int size, CancellationToken ct, string? cursor = null)
    {
        Page(page, size);
        return Public(await store.Replies(id, page, size, false, ct, cursor, ViewerKey()));
    }
    public async Task<GuestbookMessage> Get(string id, CancellationToken ct)
        => Public(await store.Get(id, false, false, ct, ViewerKey()));
    public async Task<GuestbookMessage> GetReply(string threadId, string replyId, CancellationToken ct)
        => Public(await store.GetReply(threadId, replyId, ViewerKey(), ct));
    public Task<GuestbookLikeResult> Like(string id, bool liked, CancellationToken ct)
    {
        var viewerKey = ViewerKey(required: true) ?? throw new ForbiddenException("請重新載入留言板後再試。", "GUESTBOOK_VISITOR_REQUIRED");
        return store.SetLike(id, viewerKey, liked, ct);
    }
    public async Task<GuestbookMessage> Create(string? thread, GuestbookWrite write, CancellationToken ct)
    {
        var name = Text(string.IsNullOrWhiteSpace(write.DisplayName) ? "匿名旅人" : write.DisplayName, 60);
        var content = Text(write.Content, 2000);
        var key = VisitorIpKey();
        // Return a plausible receipt without storing honeypot submissions.
        if (!string.IsNullOrEmpty(write.Website)) return new GuestbookMessage { Id = Guid.NewGuid().ToString(), DisplayName = name, Content = content, ThreadId = thread ?? "", IsVisible = true, AllowReplies = true, CreatedAt = DateTime.UtcNow.AddHours(8) };
        CustomerProfileDto? profile = null;
        byte[]? image = null;
        if (write.ImageBase64 is not null)
        {
            if (!string.IsNullOrWhiteSpace(write.CustomerUid))
                profile = await identities.ResolveUid(write.CustomerUid, ct);
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
    private string VisitorIpKey()
    {
        var visitor = TrustedVisitor(required: true)
            ?? throw new ForbiddenException("請由網站留言板送出留言。", "GUESTBOOK_PROXY_REQUIRED");
        return visitor.IpKey;
    }

    private string? ViewerKey(bool required = false)
        => TrustedVisitor(required)?.ViewerKey;

    private (string IpKey, string? ViewerKey)? TrustedVisitor(bool required)
    {
        var http = context.HttpContext ?? throw new InvalidOperationException();
        var ipText = http.Request.Headers["X-Guestbook-IP"].ToString();
        var timestamp = http.Request.Headers["X-Guestbook-Time"].ToString();
        var signature = http.Request.Headers["X-Guestbook-Signature"].ToString();
        var browserId = http.Request.Headers["X-Guestbook-Visitor-Id"].ToString();
        if (string.IsNullOrWhiteSpace(ipText) && string.IsNullOrWhiteSpace(timestamp) && string.IsNullOrWhiteSpace(signature))
        {
            if (required) throw new ForbiddenException("請由網站留言板送出留言。", "GUESTBOOK_PROXY_REQUIRED");
            return null;
        }
        var secret = config["Guestbook:ProxySecret"] ?? "";
        if (secret.Length < 32) throw new GuestbookUnavailableException();
        if (!IPAddress.TryParse(ipText, out var ip) || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var time) || Math.Abs((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - time) > 60)
            throw new ForbiddenException("請由網站留言板送出留言。", "GUESTBOOK_PROXY_REQUIRED");
        if (!string.IsNullOrEmpty(browserId) && !Guid.TryParseExact(browserId, "D", out _))
            throw new ForbiddenException("留言來源驗證失敗。", "GUESTBOOK_PROXY_REQUIRED");
        if (required && string.IsNullOrEmpty(browserId))
            throw new ForbiddenException("請重新載入留言板後再試。", "GUESTBOOK_VISITOR_REQUIRED");
        var signedValue = string.IsNullOrEmpty(browserId) ? $"{timestamp}\n{ipText}" : $"{timestamp}\n{ipText}\n{browserId}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedValue));
        byte[] received;
        try { received = Convert.FromHexString(signature); } catch (FormatException) { throw new ForbiddenException("留言來源驗證失敗。", "GUESTBOOK_PROXY_REQUIRED"); }
        if (!CryptographicOperations.FixedTimeEquals(expected, received)) throw new ForbiddenException("留言來源驗證失敗。", "GUESTBOOK_PROXY_REQUIRED");
        var normalized = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        var ipKey = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("visitor:" + normalized)));
        var viewerKey = string.IsNullOrEmpty(browserId)
            ? null
            : Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("guestbook-viewer:" + browserId)));
        return (ipKey, viewerKey);
    }
}
