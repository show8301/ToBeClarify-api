namespace ToBeClarify.Api.Auth;

public sealed class OneTimeTokenOptions
{
    public const string SectionName = "OneTimeToken";

    public string FilePath { get; set; } = "oneTimeToken.txt";

    public int ExpirationMinutes { get; set; } = 10;
}
