using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Reservations;

public interface IReservationService
{
    Task<IReadOnlyList<StaffReservationDto>> GetStaffReservationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
