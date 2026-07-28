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
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId,
                   M.`ROLE_TITLE` AS RoleTitle, M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
                   COALESCE(S.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN 'off'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN 'busy'
                        ELSE 'available' END AS CurrentStatus,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN '未上班'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN '指名中'
                        ELSE '待命中' END AS StatusText,
                   NULL AS TodayShift
            FROM `STAFF_MEMBERS` M
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID` = M.`ID` AND S.`WORK_DATE` = CURRENT_DATE()
            WHERE M.`IS_ACTIVE` = TRUE
            ORDER BY M.`SORT_ORDER`, M.`DISPLAY_NAME`
            LIMIT @Limit;
            """;
        return await QueryAsync<StaffRow>(sql, new { Limit = limit ?? int.MaxValue }, cancellationToken);
    }

    public async Task<StaffRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId,
                   M.`ROLE_TITLE` AS RoleTitle, M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
                   COALESCE(S.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN 'off'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN 'busy'
                        ELSE 'available' END AS CurrentStatus,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN '未上班'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN '指名中'
                        ELSE '待命中' END AS StatusText,
                   NULL AS TodayShift
            FROM `STAFF_MEMBERS` M
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID` = M.`ID` AND S.`WORK_DATE` = CURRENT_DATE()
            WHERE M.`ID` = @Id AND M.`IS_ACTIVE` = TRUE LIMIT 1;
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
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `MEDIA_ID` AS MediaId,
                   `SORT_ORDER` AS SortOrder
            FROM `STAFF_GALLERY_ITEMS`
            WHERE `STAFF_ID` IN @StaffIds AND `IS_PUBLISHED` = TRUE
            ORDER BY `STAFF_ID`, `SORT_ORDER`, `CREATED_AT`;
            """;
        return await QueryAsync<StaffGalleryItemRow>(sql, new { StaffIds = staffIds }, cancellationToken);
    }
}
