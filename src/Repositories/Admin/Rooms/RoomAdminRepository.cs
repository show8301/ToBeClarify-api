using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Rooms;

public sealed class RoomAdminRepository : DapperRepositoryBase, IRoomAdminRepository
{
    public RoomAdminRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public Task<IReadOnlyList<RoomRow>> GetRoomsAsync(CancellationToken cancellationToken)
        => QueryAsync<RoomRow>(RoomSelectSql + " ORDER BY R.`SORT_ORDER`, R.`ROOM_NAME`;", null, cancellationToken);

    public Task<RoomRow?> GetRoomAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<RoomRow>(RoomSelectSql + " WHERE R.`ID` = @Id LIMIT 1;", new { Id = id }, cancellationToken);

    public Task<IReadOnlyList<RoomPhotoRow>> GetRoomPhotosAsync(string roomId, CancellationToken cancellationToken)
        => QueryAsync<RoomPhotoRow>("""
            SELECT `ID` AS Id, `ROOM_ID` AS RoomId, `MEDIA_ID` AS MediaId, `SORT_ORDER` AS SortOrder
            FROM `ROOM_PHOTO_ITEMS`
            WHERE `ROOM_ID` = @RoomId
            ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, new { RoomId = roomId }, cancellationToken);

    public Task<RoomStaffOptionRow?> GetStaffOptionAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<RoomStaffOptionRow>("""
            SELECT `ID` AS Id, `DISPLAY_NAME` AS DisplayName
            FROM `STAFF_MEMBERS`
            WHERE `ID` = @Id AND `IS_ACTIVE` = TRUE
            LIMIT 1;
            """, new { Id = id }, cancellationToken);

    public async Task<int> GetSegmentMinutesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<int>(new CommandDefinition("""
            SELECT COALESCE((SELECT `SEGMENT_MINUTES` FROM `ORDERING_SETTINGS` WHERE `ID` = 'default' LIMIT 1), 20);
            """, cancellationToken: cancellationToken));
    }

    public async Task UpsertRoomAsync(string id, string roomName, string shortDescription, string? detailContent,
        string? ownerStaffId, int segmentPrice, bool isActive, int sortOrder,
        IReadOnlyList<SaveRoomPhotoRequest> photos, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `ROOMS`
                    (`ID`, `ROOM_NAME`, `SHORT_DESCRIPTION`, `DETAIL_CONTENT`, `OWNER_STAFF_ID`, `SEGMENT_PRICE`,
                     `IS_ACTIVE`, `SORT_ORDER`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @RoomName, @ShortDescription, @DetailContent, @OwnerStaffId, @SegmentPrice,
                        @IsActive, @SortOrder, @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE
                    `ROOM_NAME` = VALUES(`ROOM_NAME`),
                    `SHORT_DESCRIPTION` = VALUES(`SHORT_DESCRIPTION`),
                    `DETAIL_CONTENT` = VALUES(`DETAIL_CONTENT`),
                    `OWNER_STAFF_ID` = VALUES(`OWNER_STAFF_ID`),
                    `SEGMENT_PRICE` = VALUES(`SEGMENT_PRICE`),
                    `IS_ACTIVE` = VALUES(`IS_ACTIVE`),
                    `SORT_ORDER` = VALUES(`SORT_ORDER`),
                    `UPDATED_AT` = VALUES(`UPDATED_AT`),
                    `UPDATED_BY` = VALUES(`UPDATED_BY`);
                """, new { Id = id, RoomName = roomName, ShortDescription = shortDescription,
                    DetailContent = detailContent, OwnerStaffId = ownerStaffId, SegmentPrice = segmentPrice,
                    IsActive = isActive, SortOrder = sortOrder, Now = now, ActorId = actorId },
                transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM `ROOM_PHOTO_ITEMS` WHERE `ROOM_ID` = @RoomId;",
                new { RoomId = id }, transaction, cancellationToken: cancellationToken));

            foreach (var photo in photos)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `ROOM_PHOTO_ITEMS`
                        (`ID`, `ROOM_ID`, `MEDIA_ID`, `SORT_ORDER`, `CREATED_AT`, `CREATED_BY`)
                    VALUES (@Id, @RoomId, @MediaId, @SortOrder, @Now, @ActorId);
                    """, new { Id = Guid.NewGuid().ToString("D"), RoomId = id,
                        MediaId = photo.MediaId, SortOrder = photo.SortOrder, Now = now, ActorId = actorId },
                    transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> HasActiveRoomServiceOrdersAsync(string roomId, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1
                FROM `ROOM_SERVICE_ORDERS`
                WHERE `ROOM_ID` = @RoomId
                  AND `ORDER_STATUS` IN ('scheduled', 'in_service')
            );
            """, new { RoomId = roomId }, cancellationToken: cancellationToken));
    }

    public async Task DeleteRoomAsync(string roomId, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM `ROOM_PHOTO_ITEMS` WHERE `ROOM_ID` = @RoomId;",
                new { RoomId = roomId }, transaction, cancellationToken: cancellationToken));

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM `ROOMS` WHERE `ID` = @RoomId;",
                new { RoomId = roomId }, transaction, cancellationToken: cancellationToken));
            if (affected == 0) throw new InvalidOperationException("Room not found.");

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<RoomProfitSharingSettingsRow?> GetProfitSharingSettingsAsync(CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<RoomProfitSharingSettingsRow>("""
            SELECT `ID` AS Id, `COMMON_ROOM_STAFF_PERCENTAGE` AS CommonRoomStaffPercentage,
                   `DEDICATED_ROOM_STAFF_PERCENTAGE` AS DedicatedRoomStaffPercentage
            FROM `ROOM_PROFIT_SHARING_SETTINGS`
            WHERE `ID` = 'default'
            LIMIT 1;
            """, null, cancellationToken);

    public async Task SaveProfitSharingSettingsAsync(int commonPercentage, int dedicatedPercentage, string actorId,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `ROOM_PROFIT_SHARING_SETTINGS`
                (`ID`, `COMMON_ROOM_STAFF_PERCENTAGE`, `DEDICATED_ROOM_STAFF_PERCENTAGE`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES ('default', @CommonPercentage, @DedicatedPercentage, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `COMMON_ROOM_STAFF_PERCENTAGE` = VALUES(`COMMON_ROOM_STAFF_PERCENTAGE`),
                `DEDICATED_ROOM_STAFF_PERCENTAGE` = VALUES(`DEDICATED_ROOM_STAFF_PERCENTAGE`),
                `UPDATED_AT` = VALUES(`UPDATED_AT`),
                `UPDATED_BY` = VALUES(`UPDATED_BY`);
            """, new { CommonPercentage = commonPercentage, DedicatedPercentage = dedicatedPercentage,
                Now = now, ActorId = actorId }, cancellationToken: cancellationToken));
    }

    public Task<IReadOnlyList<RoomServiceOrderRow>> GetRoomServiceOrdersAsync(DateOnly? businessDate, string? status,
        CancellationToken cancellationToken)
        => QueryAsync<RoomServiceOrderRow>("""
            SELECT `ID` AS Id, `ORDER_ID` AS OrderId, `ORDER_ITEM_ID` AS OrderItemId,
                   `ROOM_ID` AS RoomId, `ROOM_NAME_SNAPSHOT` AS RoomNameSnapshot,
                   `BUSINESS_DATE` AS BusinessDate, `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt,
                   `SEGMENT_COUNT` AS SegmentCount, `SEGMENT_MINUTES_SNAPSHOT` AS SegmentMinutesSnapshot,
                   `UNIT_PRICE` AS UnitPrice, `TOTAL_AMOUNT` AS TotalAmount,
                   `ORDER_STATUS` AS OrderStatus, `NOTE` AS Note, `CREATED_AT` AS CreatedAt
            FROM `ROOM_SERVICE_ORDERS`
            WHERE (@BusinessDate IS NULL OR `BUSINESS_DATE` = @BusinessDate)
              AND (@Status IS NULL OR `ORDER_STATUS` = @Status)
            ORDER BY `STARTS_AT` DESC, `CREATED_AT` DESC;
            """, new { BusinessDate = businessDate?.ToDateTime(TimeOnly.MinValue), Status = status }, cancellationToken);

    public Task<RoomRow?> GetActiveRoomAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<RoomRow>(RoomSelectSql + " WHERE R.`ID` = @Id AND R.`IS_ACTIVE` = TRUE LIMIT 1;", new { Id = id }, cancellationToken);

    public async Task<bool> HasRoomServiceOrderConflictAsync(string roomId, DateTime startsAt, DateTime endsAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1
                FROM `ROOM_SERVICE_ORDERS`
                WHERE `ROOM_ID` = @RoomId
                  AND `ORDER_STATUS` IN ('scheduled', 'in_service')
                  AND `STARTS_AT` < @EndsAt
                  AND `ENDS_AT` > @StartsAt
            );
            """, new { RoomId = roomId, StartsAt = startsAt, EndsAt = endsAt },
            cancellationToken: cancellationToken));
    }

    public async Task<string> CreateRoomServiceOrderAsync(RoomRow room, DateTime businessDate, DateTime startsAt,
        DateTime endsAt, int segmentCount, int segmentMinutes, string? note, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("D");
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `ROOM_SERVICE_ORDERS`
                (`ID`, `ROOM_ID`, `ROOM_NAME_SNAPSHOT`, `BUSINESS_DATE`, `STARTS_AT`, `ENDS_AT`, `SEGMENT_COUNT`,
                 `SEGMENT_MINUTES_SNAPSHOT`, `UNIT_PRICE`, `TOTAL_AMOUNT`, `ORDER_STATUS`, `NOTE`, `CREATED_AT`,
                 `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @RoomId, @RoomName, @BusinessDate, @StartsAt, @EndsAt, @SegmentCount, @SegmentMinutes,
                    @UnitPrice, @TotalAmount, 'scheduled', @Note, @Now, @ActorId, @Now, @ActorId);
            """, new { Id = id, RoomId = room.Id, RoomName = room.RoomName, BusinessDate = businessDate,
                StartsAt = startsAt, EndsAt = endsAt, SegmentCount = segmentCount, SegmentMinutes = segmentMinutes,
                UnitPrice = room.SegmentPrice, TotalAmount = checked(room.SegmentPrice * segmentCount), Note = note,
                Now = now, ActorId = actorId }, cancellationToken: cancellationToken));
        return id;
    }

    public async Task UpdateRoomServiceOrderStatusAsync(string id, string status, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ROOM_SERVICE_ORDERS`
            SET `ORDER_STATUS` = @Status, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @Id;
            """, new { Id = id, Status = status, Now = now, ActorId = actorId }, cancellationToken: cancellationToken));
        if (affected == 0) throw new InvalidOperationException("Room service order not found.");
    }

    private const string RoomSelectSql = """
        SELECT R.`ID` AS Id, R.`ROOM_NAME` AS RoomName, R.`SHORT_DESCRIPTION` AS ShortDescription,
               R.`DETAIL_CONTENT` AS DetailContent, R.`OWNER_STAFF_ID` AS OwnerStaffId,
               S.`DISPLAY_NAME` AS OwnerStaffName, R.`SEGMENT_PRICE` AS SegmentPrice,
               R.`IS_ACTIVE` AS IsActive, R.`SORT_ORDER` AS SortOrder
        FROM `ROOMS` R
        LEFT JOIN `STAFF_MEMBERS` S ON S.`ID` = R.`OWNER_STAFF_ID`
        """;
}
