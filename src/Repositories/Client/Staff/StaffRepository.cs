using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Services.Ordering;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Client.Staff;

public sealed class StaffRepository : DapperRepositoryBase, IStaffRepository
{
    private readonly IBusinessDayContext _businessDays;
    private readonly IAppClock _clock;

    public StaffRepository(AppDbContext dbContext, IBusinessDayContext businessDays, IAppClock clock) : base(dbContext)
    {
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StaffRow>> GetStaffMembersAsync(int? limit, CancellationToken cancellationToken, DateOnly? businessDate = null)
    {
        var day = businessDate.HasValue
            ? await _businessDays.GetForDateAsync(businessDate.Value, cancellationToken)
            : await _businessDays.GetCurrentAsync(cancellationToken);
        const string sql = $"""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`SIGNATURE_MEDIA_ID` AS SignatureMediaId,
                   M.`ROLE_TITLE` AS RoleTitle, M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
                   (M.`IS_NOMINATABLE` AND COALESCE(D.`STOP_ACCEPTING_NEW_ORDERS`, FALSE) = FALSE) AS IsNominatable,
                   {StaffDutySql.IsOnDuty} AS IsWorkingToday,
                   CASE WHEN {StaffDutySql.IsOnDuty} = FALSE THEN 'off'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= @Now AND R.`ENDS_AT` > @Now)
                          OR EXISTS (SELECT 1 FROM `STAFF_BUSY_BLOCKS` B WHERE B.`STAFF_ID` = M.`ID` AND B.`BLOCK_STATUS` = 'active' AND B.`STARTS_AT` <= @Now AND B.`ENDS_AT` > @Now) THEN 'busy'
                        ELSE 'available' END AS CurrentStatus,
                   CASE WHEN {StaffDutySql.IsOnDuty} = FALSE THEN '未執勤'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= @Now AND R.`ENDS_AT` > @Now)
                          OR EXISTS (SELECT 1 FROM `STAFF_BUSY_BLOCKS` B WHERE B.`STAFF_ID` = M.`ID` AND B.`BLOCK_STATUS` = 'active' AND B.`STARTS_AT` <= @Now AND B.`ENDS_AT` > @Now) THEN '指名中'
                        ELSE '待命中' END AS StatusText,
                   CASE WHEN P.`START_TIME` IS NULL OR P.`END_TIME` IS NULL THEN NULL
                        ELSE CONCAT(DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`START_TIME`), '%m/%d %H:%i'), ' - ',
                            DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`END_TIME`) + INTERVAL (CASE WHEN P.`END_TIME` <= P.`START_TIME` THEN 1 ELSE 0 END) DAY, '%m/%d %H:%i')) END AS TodayShift
            FROM `STAFF_MEMBERS` M
            {StaffDutySql.PlanJoin}
            LEFT JOIN `STAFF_DAILY_WORK_MODES` D ON D.`STAFF_MEMBER_ID` = M.`ID`
                AND D.`BUSINESS_DATE` = COALESCE(P.`BUSINESS_DATE`, @BusinessDate)
            WHERE M.`IS_ACTIVE` = TRUE
            ORDER BY M.`SORT_ORDER`, M.`DISPLAY_NAME`
            LIMIT @Limit;
            """;
        var now = _clock.LocalDateTime;
        return await QueryAsync<StaffRow>(sql, new { Limit = limit ?? int.MaxValue, BusinessDate = day.BusinessDate.ToDateTime(TimeOnly.MinValue), UseCurrentPlan = !businessDate.HasValue, DutyAt = now, Now = now }, cancellationToken);
    }

    public async Task<StaffRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken, DateOnly? businessDate = null)
    {
        var day = businessDate.HasValue
            ? await _businessDays.GetForDateAsync(businessDate.Value, cancellationToken)
            : await _businessDays.GetCurrentAsync(cancellationToken);
        const string sql = $"""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`SIGNATURE_MEDIA_ID` AS SignatureMediaId,
                   M.`ROLE_TITLE` AS RoleTitle, M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
                   (M.`IS_NOMINATABLE` AND COALESCE(D.`STOP_ACCEPTING_NEW_ORDERS`, FALSE) = FALSE) AS IsNominatable,
                   {StaffDutySql.IsOnDuty} AS IsWorkingToday,
                   CASE WHEN {StaffDutySql.IsOnDuty} = FALSE THEN 'off'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= @Now AND R.`ENDS_AT` > @Now)
                          OR EXISTS (SELECT 1 FROM `STAFF_BUSY_BLOCKS` B WHERE B.`STAFF_ID` = M.`ID` AND B.`BLOCK_STATUS` = 'active' AND B.`STARTS_AT` <= @Now AND B.`ENDS_AT` > @Now) THEN 'busy'
                        ELSE 'available' END AS CurrentStatus,
                   CASE WHEN {StaffDutySql.IsOnDuty} = FALSE THEN '未執勤'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= @Now AND R.`ENDS_AT` > @Now)
                          OR EXISTS (SELECT 1 FROM `STAFF_BUSY_BLOCKS` B WHERE B.`STAFF_ID` = M.`ID` AND B.`BLOCK_STATUS` = 'active' AND B.`STARTS_AT` <= @Now AND B.`ENDS_AT` > @Now) THEN '指名中'
                        ELSE '待命中' END AS StatusText,
                   CASE WHEN P.`START_TIME` IS NULL OR P.`END_TIME` IS NULL THEN NULL
                        ELSE CONCAT(DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`START_TIME`), '%m/%d %H:%i'), ' - ',
                            DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`END_TIME`) + INTERVAL (CASE WHEN P.`END_TIME` <= P.`START_TIME` THEN 1 ELSE 0 END) DAY, '%m/%d %H:%i')) END AS TodayShift
            FROM `STAFF_MEMBERS` M
            {StaffDutySql.PlanJoin}
            LEFT JOIN `STAFF_DAILY_WORK_MODES` D ON D.`STAFF_MEMBER_ID` = M.`ID`
                AND D.`BUSINESS_DATE` = COALESCE(P.`BUSINESS_DATE`, @BusinessDate)
            WHERE M.`ID` = @Id AND M.`IS_ACTIVE` = TRUE LIMIT 1;
            """;
        var now = _clock.LocalDateTime;
        return await QuerySingleOrDefaultAsync<StaffRow>(sql, new { Id = id, BusinessDate = day.BusinessDate.ToDateTime(TimeOnly.MinValue), UseCurrentPlan = !businessDate.HasValue, DutyAt = now, Now = now }, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffServiceRow>> GetStaffServicesAsync(string staffId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `SERVICE_TYPE` AS ServiceType,
                   `SERVICE_NAME` AS ServiceName, `SERVICE_DESCRIPTION` AS ServiceDescription,
                   `PRICE_TEXT` AS PriceText, `PRICE` AS Price, `DURATION_MINUTES` AS DurationMinutes,
                   `IS_NOMINATABLE` AS IsNominatable,
                   `ADDITIONAL_PERSON_PRICE` AS AdditionalPersonPrice, `SORT_ORDER` AS SortOrder
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
                   `PRICE_TEXT` AS PriceText, `PRICE` AS Price, `DURATION_MINUTES` AS DurationMinutes,
                   `IS_NOMINATABLE` AS IsNominatable,
                   `ADDITIONAL_PERSON_PRICE` AS AdditionalPersonPrice, `SORT_ORDER` AS SortOrder
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
