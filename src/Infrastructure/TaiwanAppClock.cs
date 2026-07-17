namespace ToBeClarify.Api.Infrastructure;

public sealed class TaiwanAppClock : IAppClock
{
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    public DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(TaiwanOffset);

    public DateTime LocalDateTime => Now.DateTime;
}
