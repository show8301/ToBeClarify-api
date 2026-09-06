using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Rooms;

public interface IRoomRepository
{
    Task<IReadOnlyList<RoomRow>> GetRoomsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomPhotoRow>> GetRoomPhotosAsync(IReadOnlyCollection<string> roomIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomStatusRow>> GetRoomStatusesAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
    Task<int> GetSegmentMinutesAsync(CancellationToken cancellationToken);
}
