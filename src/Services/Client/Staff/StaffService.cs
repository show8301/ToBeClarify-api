using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Staff;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Staff;

public sealed class StaffService : IStaffService
{
    private readonly IStaffRepository _repository;

    public StaffService(IStaffRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<StaffListItemDto>> GetStaffAsync(int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new BusinessException("Limit must be between 1 and 100.", "INVALID_LIMIT");
        var rows = await _repository.GetStaffMembersAsync(limit, cancellationToken);
        var services = await _repository.GetStaffServicesAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => ClientContentMappings.MapStaffListItem(row, services.Where(service => service.StaffId == row.Id))).ToArray();
    }

    public async Task<StaffDetailDto> GetStaffDetailAsync(string id, CancellationToken cancellationToken)
    {
        var staffId = ClientContentMappings.RequiredId(id);
        var row = await _repository.GetStaffMemberAsync(staffId, cancellationToken)
            ?? throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        var servicesTask = _repository.GetStaffServicesAsync(staffId, cancellationToken);
        var galleryTask = _repository.GetStaffGalleryItemsAsync([staffId], cancellationToken);
        await Task.WhenAll(servicesTask, galleryTask);
        var services = await servicesTask;
        var gallery = await galleryTask;
        return new StaffDetailDto(row.Id, row.DisplayName, row.Nickname, row.AvatarUrl, row.RoleTitle,
            row.ShortBio, row.ProfileBio, row.CurrentStatus, row.StatusText, row.TodayShift,
            gallery.Select(item => new StaffGalleryItemDto(item.Id, item.ImageUrl)).ToArray(),
            services.Where(service => service.ServiceType == "common").Select(ClientContentMappings.MapStaffService).ToArray(),
            services.Where(service => service.ServiceType == "special").Select(ClientContentMappings.MapStaffService).ToArray());
    }

    public async Task<IReadOnlyList<StaffServiceDto>> GetStaffServicesAsync(string id, CancellationToken cancellationToken)
    {
        var staffId = ClientContentMappings.RequiredId(id);
        if (await _repository.GetStaffMemberAsync(staffId, cancellationToken) is null)
            throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        var rows = await _repository.GetStaffServicesAsync(staffId, cancellationToken);
        return rows.Select(ClientContentMappings.MapStaffService).ToArray();
    }
}
