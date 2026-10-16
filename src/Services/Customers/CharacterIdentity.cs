using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Services.Customers;

// GAME_ID remains the persisted identity and token snapshot. Never rewrite an
// existing session when adding a server; its issued credentials depend on it.
public sealed record CharacterIdentity(string GameId, string CharacterName, string? ServerName)
{
    public static readonly IReadOnlyList<string> Servers = Array.AsReadOnly(new[]
    {
        "伊弗利特", "利維坦", "奧汀", "巴哈姆特", "泰坦", "迦樓羅", "鳳凰"
    });

    public static CharacterIdentity Parse(string? gameId, string? serverName = null, bool requireServer = false)
    {
        var original = (gameId ?? "").Trim();
        if (original.Length is 0 or > 100)
            throw new BusinessException("請填寫人物 ID，完整顧客 ID 不得超過 100 字元。", "GAME_ID_INVALID");

        var parts = original.Replace('＠', '@').Split('@');
        if (parts.Length > 2 || parts[0].Trim().Length == 0)
            throw new BusinessException("人物 ID 格式不正確。", "GAME_ID_INVALID");

        var server = serverName is null ? null : CanonicalServer(serverName);
        if (parts.Length == 2)
        {
            var pastedServer = CanonicalServer(parts[1]);
            if (server is not null && server != pastedServer)
                throw new BusinessException("人物 ID 內的伺服器與選單不一致，請核對。", "GAME_SERVER_MISMATCH");
            server = pastedServer;
        }

        if (server is null)
        {
            if (requireServer)
                throw new BusinessException("請選擇角色所屬伺服器。", "GAME_SERVER_REQUIRED");
            // Preserve legacy whitespace for old session recovery/token parity.
            return new(original, original, null);
        }

        var name = string.Join(' ', parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var combined = $"{name}@{server}";
        if (combined.Length > 100)
            throw new BusinessException("完整顧客 ID 不得超過 100 字元。", "GAME_ID_TOO_LONG");
        return new(combined, name, server);
    }

    private static string CanonicalServer(string value)
    {
        var server = value.Trim();
        if (server.Length == 0)
            throw new BusinessException("請選擇角色所屬伺服器。", "GAME_SERVER_REQUIRED");
        if (server == "奧丁") server = "奧汀";
        if (!Servers.Contains(server, StringComparer.Ordinal))
            throw new BusinessException("伺服器不在允許清單內。", "GAME_SERVER_INVALID");
        return server;
    }
}
