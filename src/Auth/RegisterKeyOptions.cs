namespace ToBeClarify.Api.Auth;

public sealed class RegisterKeyOptions
{
    public const string SectionName = "RegisterKey";

    public string FilePath { get; set; } = "registerKey.txt";

    public int ExpirationMinutes { get; set; } = 10;
}
