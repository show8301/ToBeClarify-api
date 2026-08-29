namespace ToBeClarify.Api.Auth;

public sealed record OrderTokenPayload(string SessionId, string GameId, DateOnly BusinessDate);

public interface IOrderingTokenService
{
    string Create(string sessionId, string gameId, DateOnly businessDate);
    OrderTokenPayload Read(string token);
    string Hash(string value);
    string CreateRecoveryCode();
    string BuildOrderUrl(string token);
}
