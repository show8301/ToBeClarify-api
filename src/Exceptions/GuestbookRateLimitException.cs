namespace ToBeClarify.Api.Exceptions;

public sealed class GuestbookRateLimitException(int retryAfter) : Exception("請稍候再留言。")
{
    public int RetryAfter { get; } = retryAfter;
}
