using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Reservations;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Reservations;

public sealed class ReservationService : IReservationService
{
    private readonly IReservationRepository _repository;

    public ReservationService(IReservationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<StaffReservationDto>> GetStaffReservationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from) throw new BusinessException("The end of the range must be after the start.", "INVALID_TIME_RANGE");
        if (to - from > TimeSpan.FromDays(31))
            throw new BusinessException("The reservation range cannot exceed 31 days.", "RESERVATION_RANGE_TOO_LARGE");
        var rows = await _repository.GetStaffReservationsAsync(ClientContentMappings.ToTaiwanDateTime(from)!.Value,
            ClientContentMappings.ToTaiwanDateTime(to)!.Value, cancellationToken);
        return rows.Select(row => new StaffReservationDto(row.Id, row.StaffId, row.StaffNameSnapshot,
            row.StaffAvatarSnapshot, row.ReservationStatus, ClientContentMappings.ToTaiwanOffset(row.StartsAt),
            ClientContentMappings.ToTaiwanOffset(row.EndsAt), row.ServiceLabel)).ToArray();
    }
}
