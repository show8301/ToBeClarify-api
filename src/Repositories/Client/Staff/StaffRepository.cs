using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Staff;

public sealed class StaffRepository : DapperRepositoryBase, IStaffRepository
{
    public StaffRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<StaffRow>> GetStaffMembersAsync(int? limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `DISPLAY_NAME` AS DisplayName, `NICKNAME` AS Nickname, `AVATAR_URL` AS AvatarUrl,
                   `ROLE_TITLE` AS RoleTitle, `SHORT_BIO` AS ShortBio, `PROFILE_BIO` AS ProfileBio,
                   `CURRENT_STATUS` AS CurrentStatus, `STATUS_TEXT` AS StatusText, `TODAY_SHIFT` AS TodayShift
            FROM `STAFF_MEMBERS`
            WHERE `IS_ACTIVE` = TRUE
            ORDER BY `SORT_ORDER`, `DISPLAY_NAME`
            LIMIT @Limit;
            """;
        return await QueryAsync<StaffRow>(sql, new { Limit = limit ?? int.MaxValue }, cancellationToken);
    }

    public async Task<StaffRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `DISPLAY_NAME` AS DisplayName, `NICKNAME` AS Nickname, `AVATAR_URL` AS AvatarUrl,
                   `ROLE_TITLE` AS RoleTitle, `SHORT_BIO` AS ShortBio, `PROFILE_BIO` AS ProfileBio,
                   `CURRENT_STATUS` AS CurrentStatus, `STATUS_TEXT` AS StatusText, `TODAY_SHIFT` AS TodayShift
            FROM `STAFF_MEMBERS` WHERE `ID` = @Id AND `IS_ACTIVE` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<StaffRow>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffServiceRow>> GetStaffServicesAsync(string staffId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `SERVICE_TYPE` AS ServiceType,
                   `SERVICE_NAME` AS ServiceName, `SERVICE_DESCRIPTION` AS ServiceDescription,
                   `PRICE_TEXT` AS PriceText, `SORT_ORDER` AS SortOrder
            FROM `STAFF_SERVICES`
            WHERE `STAFF_ID` = @StaffId AND `IS_ENABLED` = TRUE
            ORDER BY `SORT_ORDER`, `SERVICE_NAME`;
            """;
        return await QueryAsync<StaffServiceRow>(sql, new { StaffId = staffId }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffServiceRow>> GetStaffServicesAsync(IReadOnlyCollection<string> staffIds, CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0) return Array.Empty<StaffServiceRow>();
        const string sql = """
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `SERVICE_TYPE` AS ServiceType,
                   `SERVICE_NAME` AS ServiceName, `SERVICE_DESCRIPTION` AS ServiceDescription,
                   `PRICE_TEXT` AS PriceText, `SORT_ORDER` AS SortOrder
            FROM `STAFF_SERVICES`
            WHERE `STAFF_ID` IN @StaffIds AND `IS_ENABLED` = TRUE
            ORDER BY `STAFF_ID`, `SERVICE_TYPE`, `SORT_ORDER`, `SERVICE_NAME`;
            """;
        return await QueryAsync<StaffServiceRow>(sql, new { StaffIds = staffIds }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffGalleryItemRow>> GetStaffGalleryItemsAsync(IReadOnlyCollection<string> staffIds, CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0) return Array.Empty<StaffGalleryItemRow>();
        const string sql = """
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `IMAGE_URL` AS ImageUrl, `SORT_ORDER` AS SortOrder
            FROM `STAFF_GALLERY_ITEMS`
            WHERE `STAFF_ID` IN @StaffIds AND `IS_PUBLISHED` = TRUE
            ORDER BY `STAFF_ID`, `SORT_ORDER`, `CREATED_AT`;
            """;
        return await QueryAsync<StaffGalleryItemRow>(sql, new { StaffIds = staffIds }, cancellationToken);
    }
}
