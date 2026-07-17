using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Reservations;

public sealed class ReservationRepository : DapperRepositoryBase, IReservationRepository
{
    public ReservationRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<StaffReservationRow>> GetStaffReservationsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                   `STAFF_AVATAR_SNAPSHOT` AS StaffAvatarSnapshot, `RESERVATION_STATUS` AS ReservationStatus,
                   `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt, `SERVICE_LABEL` AS ServiceLabel
            FROM `STAFF_RESERVATIONS`
            WHERE `ENDS_AT` > @From AND `STARTS_AT` < @To
            ORDER BY `STAFF_ID`, `STARTS_AT`;
            """;
        return await QueryAsync<StaffReservationRow>(sql, new { From = from, To = to }, cancellationToken);
    }
}
