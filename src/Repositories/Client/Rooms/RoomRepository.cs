using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Rooms;

public sealed class RoomRepository : DapperRepositoryBase, IRoomRepository
{
    public RoomRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public Task<IReadOnlyList<RoomRow>> GetRoomsAsync(CancellationToken cancellationToken)
        => QueryAsync<RoomRow>("""
            SELECT R.`ID` AS Id, R.`ROOM_NAME` AS RoomName, R.`SHORT_DESCRIPTION` AS ShortDescription,
                   R.`DETAIL_CONTENT` AS DetailContent, R.`OWNER_STAFF_ID` AS OwnerStaffId,
                   S.`DISPLAY_NAME` AS OwnerStaffName, R.`SEGMENT_PRICE` AS SegmentPrice,
                   R.`IS_ACTIVE` AS IsActive, R.`SORT_ORDER` AS SortOrder
            FROM `ROOMS` R
            LEFT JOIN `STAFF_MEMBERS` S ON S.`ID` = R.`OWNER_STAFF_ID`
            WHERE R.`IS_ACTIVE` = TRUE
            ORDER BY R.`SORT_ORDER`, R.`ROOM_NAME`;
            """, null, cancellationToken);

    public Task<IReadOnlyList<RoomPhotoRow>> GetRoomPhotosAsync(
        IReadOnlyCollection<string> roomIds,
        CancellationToken cancellationToken)
    {
        if (roomIds.Count == 0) return Task.FromResult<IReadOnlyList<RoomPhotoRow>>([]);
        return QueryAsync<RoomPhotoRow>("""
            SELECT `ID` AS Id, `ROOM_ID` AS RoomId, `MEDIA_ID` AS MediaId, `SORT_ORDER` AS SortOrder
            FROM `ROOM_PHOTO_ITEMS`
            WHERE `ROOM_ID` IN @RoomIds
            ORDER BY `ROOM_ID`, `SORT_ORDER`, `CREATED_AT`;
            """, new { RoomIds = roomIds }, cancellationToken);
    }

    public Task<IReadOnlyList<RoomStatusRow>> GetRoomStatusesAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
        => QueryAsync<RoomStatusRow>("""
            SELECT R.`ID` AS Id, R.`ROOM_NAME` AS RoomName,
                   R.`OWNER_STAFF_ID` AS OwnerStaffId, S.`DISPLAY_NAME` AS OwnerStaffName,
                   R.`SEGMENT_PRICE` AS SegmentPrice,
                   CASE
                     WHEN EXISTS (
                       SELECT 1 FROM `ROOM_SERVICE_ORDERS` O
                       WHERE O.`ROOM_ID` = R.`ID`
                         AND O.`ORDER_STATUS` IN ('scheduled', 'in_service')
                         AND O.`STARTS_AT` <= CURRENT_TIMESTAMP
                         AND O.`ENDS_AT` > CURRENT_TIMESTAMP
                     ) THEN 'occupied'
                     WHEN EXISTS (
                       SELECT 1 FROM `ROOM_SERVICE_ORDERS` O
                       WHERE O.`ROOM_ID` = R.`ID`
                         AND O.`ORDER_STATUS` IN ('scheduled', 'in_service')
                         AND O.`STARTS_AT` < @To
                         AND O.`ENDS_AT` > @From
                     ) THEN 'reserved'
                     ELSE 'available'
                   END AS CurrentStatus
            FROM `ROOMS` R
            LEFT JOIN `STAFF_MEMBERS` S ON S.`ID` = R.`OWNER_STAFF_ID`
            WHERE R.`IS_ACTIVE` = TRUE
            ORDER BY R.`SORT_ORDER`, R.`ROOM_NAME`;
            """, new { From = from, To = to }, cancellationToken);

    public async Task<int> GetSegmentMinutesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<int>(new CommandDefinition("""
            SELECT COALESCE((SELECT `SEGMENT_MINUTES` FROM `ORDERING_SETTINGS` WHERE `ID` = 'default' LIMIT 1), 20);
            """, cancellationToken: cancellationToken));
    }
}
