using System.Security.Cryptography;
using System.Text;

namespace ToBeClarify.Api.Services.Customers;

// Admin disclosure uses encryption; public access continues to verify the hash.
public sealed class DeliveryCodeProtector(IConfiguration configuration)
{
    private byte[] Key()
    {
        // Optional sections commonly contain empty defaults; match OrderingTokenService's fallback.
        var secret = new[] { configuration["ArtDelivery:CodeEncryptionKey"], configuration["OrderingToken:Secret"], configuration["JwtAuth:SigningKey"] }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("A stable delivery encryption secret of at least 32 characters is required.");
        return SHA256.HashData(Encoding.UTF8.GetBytes("lucid-dream:delivery-code:v1:" + secret));
    }
    public string Protect(string deliveryId, string code)
    {
        var plain = Encoding.UTF8.GetBytes(code);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(), 16);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes("delivery-code:v1:" + deliveryId));
        CryptographicOperations.ZeroMemory(plain);
        return "v1." + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Unprotect(string deliveryId, string value)
    {
        if (!value.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException("Unsupported code envelope.");
        var bytes = Convert.FromBase64String(value[3..]);
        if (bytes.Length < 29) throw new CryptographicException("Invalid code envelope.");
        var plain = new byte[bytes.Length - 28];
        try
        {
            using var aes = new AesGcm(Key(), 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain,
                Encoding.UTF8.GetBytes("delivery-code:v1:" + deliveryId));
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
