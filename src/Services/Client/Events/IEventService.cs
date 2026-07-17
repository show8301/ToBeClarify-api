using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Events;

public interface IEventService
{
    Task<IReadOnlyList<EventDto>> GetEventsAsync(string? status, DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken cancellationToken);
    Task<EventDto> GetEventAsync(string id, CancellationToken cancellationToken);
}
