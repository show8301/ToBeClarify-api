using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Auth;

public sealed class OrderingTokenService : IOrderingTokenService
{
    private readonly byte[] _encryptionKey;
    private readonly byte[] _hashKey;
    private readonly string _publicWebBaseUrl;

    public OrderingTokenService(IOptions<OrderingTokenOptions> options, IConfiguration configuration)
    {
        var configured = options.Value;
        var secret = string.IsNullOrWhiteSpace(configured.Secret)
            ? configuration[$"{JwtAuthOptions.SectionName}:SigningKey"]
            : configured.Secret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("OrderingToken:Secret must contain at least 32 characters.");

        _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes($"ordering:encrypt:{secret}"));
        _hashKey = SHA256.HashData(Encoding.UTF8.GetBytes($"ordering:hash:{secret}"));
        _publicWebBaseUrl = configured.PublicWebBaseUrl.TrimEnd('/');
    }

    public string Create(string sessionId, string gameId, DateOnly businessDate)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenEnvelope(
            1, sessionId, gameId, businessDate.ToString("yyyy-MM-dd")));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, payload, ciphertext, tag, "lucid-dream-order-v1"u8.ToArray());
        var packed = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        packed[0] = 1;
        Buffer.BlockCopy(nonce, 0, packed, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, 13, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, packed, 29, ciphertext.Length);
        return WebEncoders.Base64UrlEncode(packed);
    }

    public OrderTokenPayload Read(string token)
    {
        try
        {
            var packed = WebEncoders.Base64UrlDecode(token);
            if (packed.Length < 30 || packed[0] != 1) throw new CryptographicException();
            var nonce = packed.AsSpan(1, 12);
            var tag = packed.AsSpan(13, 16);
            var ciphertext = packed.AsSpan(29);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, "lucid-dream-order-v1"u8.ToArray());
            var envelope = JsonSerializer.Deserialize<TokenEnvelope>(plaintext)
                ?? throw new CryptographicException();
            if (envelope.V != 1 || string.IsNullOrWhiteSpace(envelope.Sid) ||
                string.IsNullOrWhiteSpace(envelope.Gid) || !DateOnly.TryParse(envelope.Day, out var day))
                throw new CryptographicException();
            return new OrderTokenPayload(envelope.Sid, envelope.Gid, day);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            throw new BusinessException("點餐碼無效，請洽店員協助。", "ORDER_TOKEN_INVALID");
        }
    }

    public string Hash(string value)
    {
        using var hmac = new HMACSHA256(_hashKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public string CreateRecoveryCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public string BuildOrderUrl(string token)
        => $"{_publicWebBaseUrl}?code={Uri.EscapeDataString(token)}";

    private sealed record TokenEnvelope(int V, string Sid, string Gid, string Day);
}
