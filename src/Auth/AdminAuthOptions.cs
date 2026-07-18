namespace ToBeClarify.Api.Auth;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public int TokenLifetimeMinutes { get; set; } = 120;
}
