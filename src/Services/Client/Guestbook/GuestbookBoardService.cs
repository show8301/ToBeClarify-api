using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Services.Client.Guestbook;

public sealed class GuestbookBoardService(GuestbookStore store, IConfiguration config, IHttpContextAccessor context)
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
    public Task<GuestbookList> List(int page, int size, CancellationToken ct, string? cursor = null)
    {
        Page(page, size);
        return store.List(page, size, false, "all", ct, cursor);
    }
    public Task<GuestbookReplies> Replies(string id, int page, int size, CancellationToken ct, string? cursor = null)
    {
        Page(page, size);
        return store.Replies(id, page, size, false, ct, cursor);
    }
    public Task<GuestbookMessage> Get(string id, CancellationToken ct) => store.Get(id, false, false, ct);
    public Task<GuestbookMessage> Create(string? thread, GuestbookWrite write, CancellationToken ct)
    {
        var name = Text(write.DisplayName, 60);
        var content = Text(write.Content, 2000);
        var key = VisitorKey();
        // Return a plausible receipt without storing honeypot submissions.
        if (!string.IsNullOrEmpty(write.Website)) return Task.FromResult(new GuestbookMessage { Id = Guid.NewGuid().ToString(), DisplayName = name, Content = content, ThreadId = thread ?? "", IsVisible = true, AllowReplies = true, CreatedAt = DateTime.UtcNow.AddHours(8) });
        return store.Create(thread, name, content, "customer", null, null, key, ct);
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
