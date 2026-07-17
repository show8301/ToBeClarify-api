using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Staff;

public interface IStaffRepository
{
    Task<IReadOnlyList<StaffRow>> GetStaffMembersAsync(int? limit, CancellationToken cancellationToken);
    Task<StaffRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffServiceRow>> GetStaffServicesAsync(string staffId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffServiceRow>> GetStaffServicesAsync(IReadOnlyCollection<string> staffIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffGalleryItemRow>> GetStaffGalleryItemsAsync(IReadOnlyCollection<string> staffIds, CancellationToken cancellationToken);
}
