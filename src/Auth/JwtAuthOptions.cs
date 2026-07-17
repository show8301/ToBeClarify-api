namespace ToBeClarify.Api.Auth;

public sealed class JwtAuthOptions
{
    public const string SectionName = "JwtAuth";

    public string Issuer { get; set; } = "ToBeClarify.Api";
    public string Audience { get; set; } = "ToBeClarify.Admin";
    public string SigningKey { get; set; } = "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_AT_LEAST_32_CHARS";
}
