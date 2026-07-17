namespace ToBeClarify.Api.Models.Entities;

public sealed class ApiLogEntry
{
    public DateTime RequestTime { get; set; }
    public string Level { get; set; } = "INFORMATION";
    public string IpAddress { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string ApiType { get; set; } = "client";
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? UserId { get; set; }
    public string? ExceptionMessage { get; set; }
}
