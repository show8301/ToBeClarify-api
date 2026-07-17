using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Events;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Events;

public sealed class EventService : IEventService
{
    private readonly IEventRepository _repository;

    public EventService(IEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<EventDto>> GetEventsAsync(string? status, DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken cancellationToken)
    {
        if (status is not null && !ClientContentMappings.EventStatuses.Contains(status))
            throw new BusinessException("Invalid event status.", "INVALID_EVENT_STATUS");
        if (from.HasValue && to.HasValue && to <= from)
            throw new BusinessException("The end of the range must be after the start.", "INVALID_TIME_RANGE");
        if (limit is < 1 or > 100) throw new BusinessException("Limit must be between 1 and 100.", "INVALID_LIMIT");

        var rows = await _repository.GetEventsAsync(status?.ToLowerInvariant(), ClientContentMappings.ToTaiwanDateTime(from),
            ClientContentMappings.ToTaiwanDateTime(to), limit, cancellationToken);
        return rows.Select(ClientContentMappings.MapEvent).ToArray();
    }

    public async Task<EventDto> GetEventAsync(string id, CancellationToken cancellationToken)
    {
        var row = await _repository.GetEventAsync(ClientContentMappings.RequiredId(id), cancellationToken)
            ?? throw new NotFoundException("Event not found.", "EVENT_NOT_FOUND");
        return ClientContentMappings.MapEvent(row);
    }
}
