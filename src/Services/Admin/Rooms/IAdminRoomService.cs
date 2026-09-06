using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Rooms;

public interface IAdminRoomService
{
    Task<IReadOnlyList<AdminRoomDto>> GetRoomsAsync(CancellationToken cancellationToken);
    Task<AdminRoomDto> SaveRoomAsync(string? id, SaveRoomRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken);
    Task<RoomProfitSharingSettingsDto> GetProfitSharingSettingsAsync(CancellationToken cancellationToken);
    Task<RoomProfitSharingSettingsDto> SaveProfitSharingSettingsAsync(SaveRoomProfitSharingSettingsRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomServiceOrderDto>> GetRoomServiceOrdersAsync(DateOnly? businessDate, string? status,
        CancellationToken cancellationToken);
    Task<RoomServiceOrderDto> CreateRoomServiceOrderAsync(CreateRoomServiceOrderRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<RoomServiceOrderDto> UpdateRoomServiceOrderStatusAsync(string id,
        UpdateRoomServiceOrderStatusRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
}
