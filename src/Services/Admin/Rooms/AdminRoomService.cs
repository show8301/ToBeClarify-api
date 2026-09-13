using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Rooms;
using ToBeClarify.Api.Services.Client.Rooms;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Admin.Rooms;

public sealed class AdminRoomService : IAdminRoomService
{
    private readonly IRoomAdminRepository _repository;
    private readonly IAppClock _clock;
    private readonly MediaUrlService _mediaUrls;
    private readonly AdminMediaUploadService _mediaUpload;

    public AdminRoomService(IRoomAdminRepository repository, IAppClock clock, MediaUrlService mediaUrls,
        AdminMediaUploadService mediaUpload)
    {
        _repository = repository;
        _clock = clock;
        _mediaUrls = mediaUrls;
        _mediaUpload = mediaUpload;
    }

    public async Task<IReadOnlyList<AdminRoomDto>> GetRoomsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetRoomsAsync(cancellationToken);
        var segmentMinutes = Math.Max(1, await _repository.GetSegmentMinutesAsync(cancellationToken));
        var result = new List<AdminRoomDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(await MapRoomAsync(row, segmentMinutes, cancellationToken));
        }
        return result;
    }

    public async Task<AdminRoomDto> SaveRoomAsync(string? id, SaveRoomRequest request, ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        ValidateRoomRequest(request);
        var isManager = CanManageRoomSettings(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : Required(id, "ROOM_ID_REQUIRED");
        var existing = string.IsNullOrWhiteSpace(id) ? null : await _repository.GetRoomAsync(entityId, cancellationToken);
        if (id is not null && existing is null)
            throw new NotFoundException("Room not found.", "ROOM_NOT_FOUND");

        var ownerStaffId = isManager ? Normalize(request.OwnerStaffId) : existing?.OwnerStaffId;
        var segmentPrice = isManager ? request.SegmentPrice ?? existing?.SegmentPrice ?? 0 : existing?.SegmentPrice ?? 0;
        if (!string.IsNullOrWhiteSpace(ownerStaffId) && await _repository.GetStaffOptionAsync(ownerStaffId, cancellationToken) is null)
            throw new NotFoundException("The selected staff member was not found.", "ROOM_OWNER_NOT_FOUND");
        if (isManager && segmentPrice <= 0)
            throw new BusinessException("Room segment price must be greater than zero.", "ROOM_PRICE_REQUIRED");

        var photos = request.Photos
            .Where(photo => !string.IsNullOrWhiteSpace(photo.MediaId))
            .GroupBy(photo => photo.MediaId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .ToArray();
        await _repository.UpsertRoomAsync(entityId, request.RoomName.Trim(), request.ShortDescription.Trim(),
            string.IsNullOrWhiteSpace(request.DetailContent) ? null : request.DetailContent.Trim(), ownerStaffId,
            segmentPrice, request.IsActive, request.SortOrder, photos, ActorId(actor), _clock.LocalDateTime,
            cancellationToken);

        var row = await _repository.GetRoomAsync(entityId, cancellationToken)
            ?? throw new NotFoundException("Room not found after saving.", "ROOM_NOT_FOUND");
        return await MapRoomAsync(row, Math.Max(1, await _repository.GetSegmentMinutesAsync(cancellationToken)), cancellationToken);
    }

    public async Task DeleteRoomAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var roomId = Required(id, "ROOM_ID_REQUIRED");
        if (await _repository.GetRoomAsync(roomId, cancellationToken) is null)
            throw new NotFoundException("Room not found.", "ROOM_NOT_FOUND");
        if (await _repository.HasActiveRoomServiceOrdersAsync(roomId, cancellationToken))
            throw new ConflictException(
                "The room has scheduled or in-service orders and cannot be deleted.",
                "ROOM_HAS_ACTIVE_SERVICE_ORDERS");

        var mediaIds = (await _repository.GetRoomPhotosAsync(roomId, cancellationToken))
            .Select(photo => photo.MediaId)
            .Where(mediaId => !string.IsNullOrWhiteSpace(mediaId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        try
        {
            await _repository.DeleteRoomAsync(roomId, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new NotFoundException("Room not found.", "ROOM_NOT_FOUND");
        }

        if (mediaIds.Length > 0)
            await _mediaUpload.CleanupUnreferencedAsync(mediaIds, actor, cancellationToken);
    }

    public async Task<RoomProfitSharingSettingsDto> GetProfitSharingSettingsAsync(CancellationToken cancellationToken)
    {
        var row = await _repository.GetProfitSharingSettingsAsync(cancellationToken)
            ?? new RoomProfitSharingSettingsRow();
        return new RoomProfitSharingSettingsDto(row.CommonRoomStaffPercentage, row.DedicatedRoomStaffPercentage);
    }

    public async Task<RoomProfitSharingSettingsDto> SaveProfitSharingSettingsAsync(
        SaveRoomProfitSharingSettingsRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.SaveProfitSharingSettingsAsync(request.CommonRoomStaffPercentage,
            request.DedicatedRoomStaffPercentage, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetProfitSharingSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoomServiceOrderDto>> GetRoomServiceOrdersAsync(DateOnly? businessDate,
        string? status, CancellationToken cancellationToken)
    {
        ValidateStatus(status);
        return (await _repository.GetRoomServiceOrdersAsync(businessDate, Normalize(status), cancellationToken))
            .Select(MapOrder).ToArray();
    }

    public async Task<RoomServiceOrderDto> CreateRoomServiceOrderAsync(CreateRoomServiceOrderRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var roomId = Required(request.RoomId, "ROOM_ID_REQUIRED");
        var room = await _repository.GetActiveRoomAsync(roomId, cancellationToken)
            ?? throw new NotFoundException("Room not found or inactive.", "ROOM_NOT_FOUND");
        if (request.BusinessDate == default || request.StartsAt == default)
            throw new BusinessException("Business date and start time are required.", "ROOM_ORDER_TIME_REQUIRED");

        var segmentMinutes = Math.Max(1, await _repository.GetSegmentMinutesAsync(cancellationToken));
        var startsAt = request.StartsAt;
        var endsAt = startsAt.AddMinutes(checked(segmentMinutes * request.SegmentCount));
        if (startsAt.Date != request.BusinessDate.Date)
            throw new BusinessException("Start time must be on the selected business date.", "ROOM_ORDER_DATE_MISMATCH");
        if (await _repository.HasRoomServiceOrderConflictAsync(room.Id, startsAt, endsAt, cancellationToken))
            throw new ConflictException("The room is already booked for the selected time.", "ROOM_ORDER_CONFLICT");
        var id = await _repository.CreateRoomServiceOrderAsync(room, request.BusinessDate.Date, startsAt, endsAt,
            request.SegmentCount, segmentMinutes, Normalize(request.Note), ActorId(actor), _clock.LocalDateTime,
            cancellationToken);
        var orders = await _repository.GetRoomServiceOrdersAsync(DateOnly.FromDateTime(request.BusinessDate), null, cancellationToken);
        return MapOrder(orders.First(order => order.Id == id));
    }

    public async Task<RoomServiceOrderDto> UpdateRoomServiceOrderStatusAsync(string id,
        UpdateRoomServiceOrderStatusRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var orderId = Required(id, "ROOM_ORDER_ID_REQUIRED");
        ValidateStatus(request.Status);
        try
        {
            await _repository.UpdateRoomServiceOrderStatusAsync(orderId, request.Status, ActorId(actor),
                _clock.LocalDateTime, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new NotFoundException("Room service order not found.", "ROOM_ORDER_NOT_FOUND");
        }
        var order = (await _repository.GetRoomServiceOrdersAsync(null, null, cancellationToken))
            .FirstOrDefault(item => item.Id == orderId)
            ?? throw new NotFoundException("Room service order not found.", "ROOM_ORDER_NOT_FOUND");
        return MapOrder(order);
    }

    private async Task<AdminRoomDto> MapRoomAsync(RoomRow row, int segmentMinutes,
        CancellationToken cancellationToken)
    {
        var photos = await _repository.GetRoomPhotosAsync(row.Id, cancellationToken);
        return new AdminRoomDto(row.Id, row.RoomName, row.ShortDescription, row.DetailContent,
            RoomService.OwnershipType(row.OwnerStaffId), row.OwnerStaffId, row.OwnerStaffName,
            segmentMinutes, row.SegmentPrice, row.IsActive, row.SortOrder,
            photos.Select(photo => new RoomPhotoDto(photo.Id, photo.MediaId,
                _mediaUrls.BuildUrl(photo.MediaId, "hero") ?? string.Empty, photo.SortOrder)).ToArray());
    }

    private static RoomServiceOrderDto MapOrder(RoomServiceOrderRow row)
        => new(row.Id, row.RoomId, row.RoomNameSnapshot, row.BusinessDate, row.StartsAt, row.EndsAt,
            row.SegmentCount, row.SegmentMinutesSnapshot, row.UnitPrice, row.TotalAmount,
            row.OrderStatus, row.Note, row.CreatedAt);

    private static void ValidateRoomRequest(SaveRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RoomName))
            throw new BusinessException("Room name is required.", "ROOM_NAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.ShortDescription))
            throw new BusinessException("Room introduction is required.", "ROOM_DESCRIPTION_REQUIRED");
        if (request.Photos.Count > 20)
            throw new BusinessException("A room can have at most 20 photos.", "ROOM_PHOTO_LIMIT");
    }

    private static bool CanManageRoomSettings(ClaimsPrincipal actor)
        => actor.IsInRole(AdminRole.Developer) || actor.IsInRole(AdminRole.Manager);

    private static void EnsureManager(ClaimsPrincipal actor)
    {
        if (!CanManageRoomSettings(actor)) throw new ForbiddenException("Only managers can update room sharing settings.", "ROOM_PROFIT_SETTINGS_FORBIDDEN");
    }

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

    private static string Required(string? value, string errorCode)
        => string.IsNullOrWhiteSpace(value) ? throw new BusinessException("An identifier is required.", errorCode) : value.Trim();

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateStatus(string? status)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("scheduled" or "in_service" or "completed" or "cancelled"))
            throw new BusinessException("Unsupported room service order status.", "ROOM_ORDER_STATUS_INVALID");
    }
}
