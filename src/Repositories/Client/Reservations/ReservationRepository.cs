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
                   `STAFF_AVATAR_MEDIA_ID` AS StaffAvatarMediaId,
                   `STAFF_AVATAR_SNAPSHOT` AS LegacyStaffAvatarSnapshot, `RESERVATION_STATUS` AS ReservationStatus,
                   `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt, `SERVICE_LABEL` AS ServiceLabel,
                   `CUSTOMER_NAME` AS CustomerName
            FROM `STAFF_RESERVATIONS`
            WHERE `ENDS_AT` > @From AND `STARTS_AT` < @To
            UNION ALL
            SELECT B.`ID` AS Id, B.`STAFF_ID` AS StaffId, N.`STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                   M.`AVATAR_MEDIA_ID` AS StaffAvatarMediaId, NULL AS LegacyStaffAvatarSnapshot,
                   CASE WHEN B.`BLOCK_STATUS` = 'active' THEN 'active' ELSE B.`BLOCK_STATUS` END AS ReservationStatus,
                   B.`STARTS_AT` AS StartsAt, B.`ENDS_AT` AS EndsAt,
                   N.`SERVICE_NAME_SNAPSHOT` AS ServiceLabel, S.`CUSTOMER_NAME` AS CustomerName
            FROM `STAFF_BUSY_BLOCKS` B
            JOIN `ORDER_NOMINEES` N ON N.`ID` = B.`ORDER_NOMINEE_ID`
            JOIN `ORDERS` O ON O.`ID` = B.`ORDER_ID`
            JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = B.`STAFF_ID`
            WHERE B.`BLOCK_STATUS` IN ('active', 'completed')
              AND B.`ENDS_AT` > @From AND B.`STARTS_AT` < @To
            ORDER BY `STAFF_ID`, `STARTS_AT`;
            """;
        return await QueryAsync<StaffReservationRow>(sql, new { From = from, To = to }, cancellationToken);
    }
}
