namespace ToBeClarify.Api.Models.Common;

public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data) => new()
    {
        Success = true,
        Data = data
    };

    public static ApiResponse<T> Fail(string errorCode, string message, string? traceId = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        Message = message,
        TraceId = traceId
    };
}
