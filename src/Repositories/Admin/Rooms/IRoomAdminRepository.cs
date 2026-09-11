using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Rooms;

public interface IRoomAdminRepository
{
    Task<IReadOnlyList<RoomRow>> GetRoomsAsync(CancellationToken cancellationToken);
    Task<RoomRow?> GetRoomAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomPhotoRow>> GetRoomPhotosAsync(string roomId, CancellationToken cancellationToken);
    Task<RoomStaffOptionRow?> GetStaffOptionAsync(string id, CancellationToken cancellationToken);
    Task<int> GetSegmentMinutesAsync(CancellationToken cancellationToken);
    Task UpsertRoomAsync(string id, string roomName, string shortDescription, string? detailContent,
        string? ownerStaffId, int segmentPrice, bool isActive, int sortOrder,
        IReadOnlyList<SaveRoomPhotoRequest> photos, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task<bool> HasActiveRoomServiceOrdersAsync(string roomId, CancellationToken cancellationToken);
    Task DeleteRoomAsync(string roomId, string actorId, DateTime now, CancellationToken cancellationToken);

    Task<RoomProfitSharingSettingsRow?> GetProfitSharingSettingsAsync(CancellationToken cancellationToken);
    Task SaveProfitSharingSettingsAsync(int commonPercentage, int dedicatedPercentage, string actorId,
        DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomServiceOrderRow>> GetRoomServiceOrdersAsync(DateOnly? businessDate, string? status,
        CancellationToken cancellationToken);
    Task<RoomRow?> GetActiveRoomAsync(string id, CancellationToken cancellationToken);
    Task<bool> HasRoomServiceOrderConflictAsync(string roomId, DateTime startsAt, DateTime endsAt,
        CancellationToken cancellationToken);
    Task<string> CreateRoomServiceOrderAsync(RoomRow room, DateTime businessDate, DateTime startsAt,
        DateTime endsAt, int segmentCount, int segmentMinutes, string? note, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task UpdateRoomServiceOrderStatusAsync(string id, string status, string actorId, DateTime now,
        CancellationToken cancellationToken);
}

public sealed class RoomStaffOptionRow
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
