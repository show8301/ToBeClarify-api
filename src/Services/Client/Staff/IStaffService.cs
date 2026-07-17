using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Staff;

public interface IStaffService
{
    Task<IReadOnlyList<StaffListItemDto>> GetStaffAsync(int? limit, CancellationToken cancellationToken);
    Task<StaffDetailDto> GetStaffDetailAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffServiceDto>> GetStaffServicesAsync(string id, CancellationToken cancellationToken);
}
