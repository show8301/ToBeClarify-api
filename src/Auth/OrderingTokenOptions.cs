namespace ToBeClarify.Api.Auth;

public sealed class OrderingTokenOptions
{
    public const string SectionName = "OrderingToken";

    public string Secret { get; set; } = string.Empty;
    public string PublicWebBaseUrl { get; set; } = "https://www-dev.marchgroup.net/order";
}
