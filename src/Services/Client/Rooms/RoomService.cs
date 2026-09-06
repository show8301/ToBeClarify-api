using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Repositories.Client.Rooms;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Client.Rooms;

public sealed class RoomService : IRoomService
{
    private readonly IRoomRepository _repository;
    private readonly MediaUrlService _mediaUrls;

    public RoomService(IRoomRepository repository, MediaUrlService mediaUrls)
    {
        _repository = repository;
        _mediaUrls = mediaUrls;
    }

    public async Task<RoomListDto> GetRoomsAsync(CancellationToken cancellationToken)
    {
        var roomsTask = _repository.GetRoomsAsync(cancellationToken);
        var segmentTask = _repository.GetSegmentMinutesAsync(cancellationToken);
        await Task.WhenAll(roomsTask, segmentTask);
        var rooms = await roomsTask;
        var photos = await _repository.GetRoomPhotosAsync(rooms.Select(room => room.Id).ToArray(), cancellationToken);
        var segmentMinutes = Math.Max(1, await segmentTask);
        return new RoomListDto(segmentMinutes, rooms.Select(room => MapRoom(room, photos, segmentMinutes)).ToArray());
    }

    public async Task<IReadOnlyList<RoomStatusDto>> GetRoomStatusesAsync(DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        if (to <= from) throw new BusinessException("The room status range must be increasing.", "ROOM_STATUS_RANGE_INVALID");
        var rows = await _repository.GetRoomStatusesAsync(from, to, cancellationToken);
        return rows.Select(row => new RoomStatusDto(
            row.Id,
            row.RoomName,
            OwnershipType(row.OwnerStaffId),
            row.OwnerStaffName,
            row.CurrentStatus,
            StatusText(row.CurrentStatus))).ToArray();
    }

    private RoomDto MapRoom(RoomRow row, IReadOnlyList<RoomPhotoRow> photos,
        int segmentMinutes)
        => new(row.Id, row.RoomName, row.ShortDescription, row.DetailContent,
            OwnershipType(row.OwnerStaffId), row.OwnerStaffId, row.OwnerStaffName,
            segmentMinutes, row.SegmentPrice,
            photos.Where(photo => photo.RoomId == row.Id)
                .Select(photo => new RoomPhotoDto(photo.Id, photo.MediaId,
                    _mediaUrls.BuildUrl(photo.MediaId, "hero") ?? string.Empty, photo.SortOrder)).ToArray());

    internal static string OwnershipType(string? ownerStaffId) => string.IsNullOrWhiteSpace(ownerStaffId) ? "common" : "dedicated";

    internal static string StatusText(string status) => status switch
    {
        "occupied" => "使用中",
        "reserved" => "已預約",
        _ => "可使用"
    };
}
