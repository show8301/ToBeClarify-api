using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Reservations;

public interface IReservationRepository
{
    Task<IReadOnlyList<StaffReservationRow>> GetStaffReservationsAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}
