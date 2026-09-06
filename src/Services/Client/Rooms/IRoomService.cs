using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Rooms;

public interface IRoomService
{
    Task<RoomListDto> GetRoomsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomStatusDto>> GetRoomStatusesAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}
