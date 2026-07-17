using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Events;

public interface IEventRepository
{
    Task<IReadOnlyList<EventRow>> GetEventsAsync(string? status, DateTime? from, DateTime? to, int? limit, CancellationToken cancellationToken);
    Task<EventRow?> GetEventAsync(string id, CancellationToken cancellationToken);
}
