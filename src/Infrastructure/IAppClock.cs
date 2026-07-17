namespace ToBeClarify.Api.Infrastructure;

public interface IAppClock
{
    DateTimeOffset Now { get; }
    DateTime LocalDateTime { get; }
}
