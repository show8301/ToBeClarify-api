namespace ToBeClarify.Api.Services.Logging;

public sealed class ApiLoggingOptions
{
    public const string SectionName = "ApiLogging";

    public int SlowRequestThresholdMs { get; set; } = 2000;
}
