using System.Text.Json;
using ToBeClarify.Api.Exceptions;
using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Services.Ordering;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Models;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Content;

// Direct SQL maintenance account cannot delete rows or schema objects. The Delete*
// methods below are API business paths; they may be invoked only through authorized
// API requests using a deployment connection identity with the required privilege.
public sealed class AdminContentRepository : DapperRepositoryBase, IAdminContentRepository
{
    private readonly IBusinessDayContext _businessDays;
    private readonly IAppClock _clock;

    public AdminContentRepository(AppDbContext dbContext, IBusinessDayContext businessDays, IAppClock clock) : base(dbContext)
    {
        _businessDays = businessDays;
        _clock = clock;
    }

    public Task<IReadOnlyList<AdminSiteSettingRow>> GetSiteSettingsAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminSiteSettingRow>("""
            SELECT `ID` AS Id, `SETTING_KEY` AS SettingKey, `SETTING_VALUE` AS SettingValue,
                   `DESCRIPTION` AS Description, `IS_ACTIVE` AS IsActive
            FROM `SITE_SETTINGS` ORDER BY `SETTING_KEY`;
            """, null, cancellationToken);

    public Task<AdminSiteSettingRow?> GetSiteSettingAsync(string settingKey, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<AdminSiteSettingRow>("""
            SELECT `ID` AS Id, `SETTING_KEY` AS SettingKey, `SETTING_VALUE` AS SettingValue,
                   `DESCRIPTION` AS Description, `IS_ACTIVE` AS IsActive
            FROM `SITE_SETTINGS` WHERE `SETTING_KEY` = @SettingKey LIMIT 1;
            """, new { SettingKey = settingKey }, cancellationToken);

    public async Task UpsertSiteSettingAsync(string id, string settingKey, SaveSiteSettingRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `SITE_SETTINGS`
                (`ID`, `SETTING_KEY`, `SETTING_VALUE`, `DESCRIPTION`, `IS_ACTIVE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @SettingKey, @SettingValue, @Description, @IsActive, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `SETTING_VALUE` = VALUES(`SETTING_VALUE`), `DESCRIPTION` = VALUES(`DESCRIPTION`),
                `IS_ACTIVE` = VALUES(`IS_ACTIVE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """;
        await ExecuteAsync(sql, new
        {
            Id = id, SettingKey = settingKey, SettingValue = request.SettingValue.GetRawText(),
            request.Description, request.IsActive, Now = now, ActorId = actorId
        }, cancellationToken);
    }

    public Task<IReadOnlyList<AdminNavigationItemRow>> GetNavigationItemsAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminNavigationItemRow>("""
            SELECT `ID` AS Id, `LABEL` AS Label, `ROUTE_PATH` AS RoutePath, `PLACEMENT` AS Placement,
                   `PARENT_ITEM_ID` AS ParentItemId, `SORT_ORDER` AS SortOrder,
                   `IS_DROPDOWN` AS IsDropdown, `IS_ENABLED` AS IsEnabled
            FROM `NAVIGATION_ITEMS` ORDER BY `SORT_ORDER`, `LABEL`;
            """, null, cancellationToken);

    public Task UpsertNavigationItemAsync(string id, SaveNavigationItemRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `NAVIGATION_ITEMS`
                (`ID`, `LABEL`, `ROUTE_PATH`, `PLACEMENT`, `PARENT_ITEM_ID`, `SORT_ORDER`, `IS_DROPDOWN`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @Label, @RoutePath, @Placement, @ParentItemId, @SortOrder, @IsDropdown, @IsEnabled, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `LABEL` = VALUES(`LABEL`), `ROUTE_PATH` = VALUES(`ROUTE_PATH`),
                `PLACEMENT` = VALUES(`PLACEMENT`), `PARENT_ITEM_ID` = VALUES(`PARENT_ITEM_ID`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_DROPDOWN` = VALUES(`IS_DROPDOWN`),
                `IS_ENABLED` = VALUES(`IS_ENABLED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.Label, request.RoutePath, request.Placement, request.ParentItemId,
                request.SortOrder, request.IsDropdown, request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeleteNavigationItemAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `NAVIGATION_ITEMS` WHERE `ID` = @Id OR `PARENT_ITEM_ID` = @Id;",
            new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public Task<IReadOnlyList<AdminHomeCarouselRow>> GetHomeCarouselsAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminHomeCarouselRow>("""
            SELECT `ID` AS Id, `ALBUM_ID` AS AlbumId, `OVERRIDE_TITLE` AS OverrideTitle,
                   `OVERRIDE_SUMMARY` AS OverrideSummary, `OVERRIDE_MEDIA_ID` AS OverrideMediaId,
                   `EVENT_TIME_SNAPSHOT` AS EventTimeSnapshot,
                   `CTA_LABEL` AS CtaLabel, `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `HOME_EVENT_CAROUSELS` ORDER BY `SORT_ORDER`, `CREATED_AT` DESC;
            """, null, cancellationToken);

    public Task UpsertHomeCarouselAsync(string id, SaveHomeCarouselRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `HOME_EVENT_CAROUSELS`
                (`ID`, `ALBUM_ID`, `OVERRIDE_TITLE`, `OVERRIDE_SUMMARY`, `OVERRIDE_MEDIA_ID`, `EVENT_TIME_SNAPSHOT`, `CTA_LABEL`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @AlbumId, @OverrideTitle, @OverrideSummary, @OverrideMediaId, @EventTimeSnapshot, @CtaLabel, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `ALBUM_ID` = VALUES(`ALBUM_ID`), `OVERRIDE_TITLE` = VALUES(`OVERRIDE_TITLE`),
                `OVERRIDE_SUMMARY` = VALUES(`OVERRIDE_SUMMARY`), `OVERRIDE_MEDIA_ID` = VALUES(`OVERRIDE_MEDIA_ID`),
                `EVENT_TIME_SNAPSHOT` = VALUES(`EVENT_TIME_SNAPSHOT`),
                `CTA_LABEL` = VALUES(`CTA_LABEL`), `SORT_ORDER` = VALUES(`SORT_ORDER`),
                `IS_ENABLED` = VALUES(`IS_ENABLED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.AlbumId, request.OverrideTitle, request.OverrideSummary, request.OverrideMediaId,
                request.EventTimeSnapshot, request.CtaLabel, request.SortOrder,
                request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeleteHomeCarouselAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `HOME_EVENT_CAROUSELS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public Task<IReadOnlyList<AdminHomeSlideRow>> GetHomeSlidesAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminHomeSlideRow>("""
            SELECT `ID` AS Id, `MEDIA_ID` AS MediaId,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled,
                   COALESCE(`DISPLAY_SECONDS`, 10) AS DisplaySeconds
            FROM `HOME_SLIDES`
            ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, null, cancellationToken);

    public Task<AdminHomeSlideRow?> GetHomeSlideAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<AdminHomeSlideRow>("""
            SELECT `ID` AS Id, `MEDIA_ID` AS MediaId,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled,
                   COALESCE(`DISPLAY_SECONDS`, 10) AS DisplaySeconds
            FROM `HOME_SLIDES` WHERE `ID` = @Id LIMIT 1;
            """, new { Id = id }, cancellationToken);

    public Task UpsertHomeSlideAsync(string id, SaveHomeSlideRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `HOME_SLIDES`
                (`ID`, `MEDIA_ID`, `SORT_ORDER`, `IS_ENABLED`, `DISPLAY_SECONDS`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @MediaId, @SortOrder, @IsEnabled, @DisplaySeconds, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `MEDIA_ID` = VALUES(`MEDIA_ID`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_ENABLED` = VALUES(`IS_ENABLED`),
                `DISPLAY_SECONDS` = VALUES(`DISPLAY_SECONDS`),
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.MediaId, request.SortOrder, request.IsEnabled, request.DisplaySeconds, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeleteHomeSlideAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `HOME_SLIDES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public Task<IReadOnlyList<AdminShopRuleRow>> GetShopRulesAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminShopRuleRow>("""
            SELECT `ID` AS Id, `RULE_TEXT` AS RuleText, `RULE_NOTE` AS RuleNote,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `SHOP_RULES` ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, null, cancellationToken);

    public Task UpsertShopRuleAsync(string id, SaveShopRuleRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `SHOP_RULES`
                (`ID`, `RULE_TEXT`, `RULE_NOTE`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @RuleText, @RuleNote, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `RULE_TEXT` = VALUES(`RULE_TEXT`), `RULE_NOTE` = VALUES(`RULE_NOTE`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_ENABLED` = VALUES(`IS_ENABLED`),
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.RuleText, request.RuleNote, request.SortOrder, request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeleteShopRuleAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `SHOP_RULES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public async Task<IReadOnlyList<AdminStaffMemberListRow>> GetStaffMembersAsync(CancellationToken cancellationToken)
    {
        var day = await _businessDays.GetCurrentAsync(cancellationToken);
        return await QueryAsync<AdminStaffMemberListRow>($"""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`ROLE_TITLE` AS RoleTitle,
                   {StaffDutySql.IsOnDuty} AS IsWorkingToday,
                   CASE WHEN P.`IS_WORKING` = TRUE AND P.`START_TIME` IS NOT NULL AND P.`END_TIME` IS NOT NULL
                        THEN CONCAT(DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`START_TIME`), '%m/%d %H:%i'), ' - ',
                            DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`END_TIME`) + INTERVAL (CASE WHEN P.`END_TIME` <= P.`START_TIME` THEN 1 ELSE 0 END) DAY, '%m/%d %H:%i'))
                        ELSE NULL END AS TodayShift,
                   M.`BUFFER_MINUTES` AS BufferMinutes, M.`IS_NOMINATABLE` AS IsNominatable,
                   M.`SORT_ORDER` AS SortOrder, M.`IS_ACTIVE` AS IsActive,
                   DATE_FORMAT(COALESCE(P.`BUSINESS_DATE`, @BusinessDate), '%Y-%m-%d') AS BusinessDate,
                   CASE WHEN EXISTS(SELECT 1 FROM STAFF_PAYROLL_SNAPSHOTS X WHERE X.STAFF_MEMBER_ID=M.ID AND X.BUSINESS_DATE=@BusinessDate)
                        THEN (SELECT X.PAYROLL_ROLE FROM STAFF_PAYROLL_SNAPSHOTS X WHERE X.STAFF_MEMBER_ID=M.ID AND X.BUSINESS_DATE=@BusinessDate)
                        ELSE P.PAYROLL_ROLE END AS PayrollRole,
                   EXISTS (SELECT 1 FROM `ADMIN_USERS` U WHERE U.`STAFF_MEMBER_ID` = M.`ID`
                       AND U.`IS_ACTIVE` = TRUE AND U.`ROLE_LEVEL` IN ('manager', 'developer')) AS CanUseManagerPayrollRole,
                   EXISTS(SELECT 1 FROM `ADMIN_USERS` U WHERE U.`STAFF_MEMBER_ID` = M.`ID`
                       AND U.`IS_ACTIVE` = TRUE AND U.`ROLE_LEVEL` = 'developer') AS CanUseBackstage,
                   COALESCE(P.`SCHEDULED_ROLES_JSON`, D.`SCHEDULED_ROLES_JSON`, '[]') AS ScheduledRolesJson,
                   COALESCE(D.`ACTIVE_ROLES_JSON`,
                       CASE WHEN {StaffDutySql.IsOnDuty} = TRUE
                            THEN COALESCE(P.`SCHEDULED_ROLES_JSON`, '[]')
                            ELSE '[]' END) AS ActiveRolesJson,
                   COALESCE(D.`STOP_ACCEPTING_NEW_ORDERS`, FALSE) AS StopAcceptingNewOrders
            FROM `STAFF_MEMBERS` M
            {StaffDutySql.PlanJoin}
            LEFT JOIN `STAFF_DAILY_WORK_MODES` D ON D.`STAFF_MEMBER_ID` = M.`ID`
                AND D.`BUSINESS_DATE` = COALESCE(P.`BUSINESS_DATE`, @BusinessDate)
            ORDER BY `SORT_ORDER`, `DISPLAY_NAME`;
            """, new { BusinessDate = day.BusinessDate.ToDateTime(TimeOnly.MinValue), UseCurrentPlan = true, DutyAt = _clock.LocalDateTime }, cancellationToken);
    }

    public async Task<AdminStaffMemberRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken)
    {
        var day = await _businessDays.GetCurrentAsync(cancellationToken);
        return await QuerySingleOrDefaultAsync<AdminStaffMemberRow>($"""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`SIGNATURE_MEDIA_ID` AS SignatureMediaId,
                   M.`ROLE_TITLE` AS RoleTitle,
                   M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
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
                            DATE_FORMAT(TIMESTAMP(P.`BUSINESS_DATE`, P.`END_TIME`) + INTERVAL (CASE WHEN P.`END_TIME` <= P.`START_TIME` THEN 1 ELSE 0 END) DAY, '%m/%d %H:%i')) END AS TodayShift,
                   M.`BUFFER_MINUTES` AS BufferMinutes,
                   M.`IS_NOMINATABLE` AS IsNominatable,
                   M.`SORT_ORDER` AS SortOrder, M.`IS_ACTIVE` AS IsActive,
                   DATE_FORMAT(COALESCE(P.`BUSINESS_DATE`, @BusinessDate), '%Y-%m-%d') AS BusinessDate,
                   CASE WHEN EXISTS(SELECT 1 FROM STAFF_PAYROLL_SNAPSHOTS X WHERE X.STAFF_MEMBER_ID=M.ID AND X.BUSINESS_DATE=@BusinessDate)
                        THEN (SELECT X.PAYROLL_ROLE FROM STAFF_PAYROLL_SNAPSHOTS X WHERE X.STAFF_MEMBER_ID=M.ID AND X.BUSINESS_DATE=@BusinessDate)
                        ELSE P.PAYROLL_ROLE END AS PayrollRole,
                   EXISTS (SELECT 1 FROM `ADMIN_USERS` U WHERE U.`STAFF_MEMBER_ID` = M.`ID`
                       AND U.`IS_ACTIVE` = TRUE AND U.`ROLE_LEVEL` IN ('manager', 'developer')) AS CanUseManagerPayrollRole,
                   EXISTS(SELECT 1 FROM `ADMIN_USERS` U WHERE U.`STAFF_MEMBER_ID` = M.`ID`
                       AND U.`IS_ACTIVE` = TRUE AND U.`ROLE_LEVEL` = 'developer') AS CanUseBackstage,
                   COALESCE(P.`SCHEDULED_ROLES_JSON`, D.`SCHEDULED_ROLES_JSON`, '[]') AS ScheduledRolesJson,
                   COALESCE(D.`ACTIVE_ROLES_JSON`,
                       CASE WHEN {StaffDutySql.IsOnDuty} = TRUE
                            THEN COALESCE(P.`SCHEDULED_ROLES_JSON`, '[]')
                            ELSE '[]' END) AS ActiveRolesJson,
                   COALESCE(D.`STOP_ACCEPTING_NEW_ORDERS`, FALSE) AS StopAcceptingNewOrders
            FROM `STAFF_MEMBERS` M
            {StaffDutySql.PlanJoin}
            LEFT JOIN `STAFF_DAILY_WORK_MODES` D ON D.`STAFF_MEMBER_ID` = M.`ID`
                AND D.`BUSINESS_DATE` = COALESCE(P.`BUSINESS_DATE`, @BusinessDate)
            WHERE M.`ID` = @Id LIMIT 1;
            """, new { Id = id, BusinessDate = day.BusinessDate.ToDateTime(TimeOnly.MinValue), UseCurrentPlan = true, DutyAt = _clock.LocalDateTime, Now = _clock.LocalDateTime }, cancellationToken);
    }

    public Task<IReadOnlyList<AdminStaffServiceRow>> GetStaffServicesAsync(string staffId, CancellationToken cancellationToken)
        => QueryAsync<AdminStaffServiceRow>("""
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `SERVICE_TYPE` AS ServiceType,
                   `SERVICE_NAME` AS ServiceName, `SERVICE_DESCRIPTION` AS ServiceDescription,
                   `PRICE_TEXT` AS PriceText, `PRICE` AS Price, `DURATION_MINUTES` AS DurationMinutes,
                   `IS_NOMINATABLE` AS IsNominatable,
                   `ADDITIONAL_PERSON_PRICE` AS AdditionalPersonPrice,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `STAFF_SERVICES` WHERE `STAFF_ID` = @StaffId
            ORDER BY `SORT_ORDER`, `SERVICE_NAME`;
            """, new { StaffId = staffId }, cancellationToken);

    public Task<IReadOnlyList<AdminStaffGalleryItemRow>> GetStaffGalleryAsync(string staffId, CancellationToken cancellationToken)
        => QueryAsync<AdminStaffGalleryItemRow>("""
            SELECT `ID` AS Id, `STAFF_ID` AS StaffId, `MEDIA_ID` AS MediaId,
                   `SORT_ORDER` AS SortOrder, `IS_PUBLISHED` AS IsPublished
            FROM `STAFF_GALLERY_ITEMS` WHERE `STAFF_ID` = @StaffId
            ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, new { StaffId = staffId }, cancellationToken);

    public async Task SaveStaffMemberAsync(string id, SaveStaffMemberRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `STAFF_MEMBERS`
            SET `DISPLAY_NAME` = @DisplayName, `NICKNAME` = @Nickname,
                `AVATAR_MEDIA_ID` = @AvatarMediaId, `SIGNATURE_MEDIA_ID` = @SignatureMediaId,
                `ROLE_TITLE` = @RoleTitle, `SHORT_BIO` = @ShortBio, `PROFILE_BIO` = @ProfileBio,
                `BUFFER_MINUTES` = @BufferMinutes, `IS_NOMINATABLE` = @IsNominatable,
                `SORT_ORDER` = @SortOrder,
                `IS_ACTIVE` = @IsActive, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @Id;
            """, new { Id = id, request.DisplayName, request.Nickname, request.AvatarMediaId, request.SignatureMediaId,
                request.RoleTitle, request.ShortBio, request.ProfileBio, request.BufferMinutes, request.IsNominatable,
                request.SortOrder, request.IsActive, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `STAFF_SERVICES` WHERE `STAFF_ID` = @StaffId;", new { StaffId = id }, transaction, cancellationToken: cancellationToken));
        foreach (var service in request.Services ?? [])
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_SERVICES`
                    (`ID`, `STAFF_ID`, `SERVICE_TYPE`, `SERVICE_NAME`, `SERVICE_DESCRIPTION`, `PRICE_TEXT`, `PRICE`, `DURATION_MINUTES`, `IS_NOMINATABLE`, `ADDITIONAL_PERSON_PRICE`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @StaffId, @ServiceType, @ServiceName, @ServiceDescription, @PriceText, @Price, @DurationMinutes, @IsNominatable, @AdditionalPersonPrice, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId);
                """, new { Id = string.IsNullOrWhiteSpace(service.Id) ? NewId() : service.Id, StaffId = id,
                    service.ServiceType, service.ServiceName, service.ServiceDescription, service.PriceText,
                    service.Price, service.DurationMinutes, service.IsNominatable, service.AdditionalPersonPrice,
                    service.SortOrder, service.IsEnabled, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `STAFF_GALLERY_ITEMS` WHERE `STAFF_ID` = @StaffId;", new { StaffId = id }, transaction, cancellationToken: cancellationToken));
        foreach (var gallery in request.Gallery ?? [])
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_GALLERY_ITEMS`
                    (`ID`, `STAFF_ID`, `MEDIA_ID`, `SORT_ORDER`, `IS_PUBLISHED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @StaffId, @MediaId, @SortOrder, @IsPublished, @Now, @ActorId, @Now, @ActorId);
                """, new { Id = string.IsNullOrWhiteSpace(gallery.Id) ? NewId() : gallery.Id, StaffId = id,
                    gallery.MediaId, gallery.SortOrder, gallery.IsPublished, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task UpdateStaffMemberStatusAsync(string id, bool isActive, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            UPDATE `STAFF_MEMBERS`
            SET `IS_ACTIVE` = @IsActive, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @Id;
            """, new { Id = id, IsActive = isActive, Now = now, ActorId = actorId }, cancellationToken);

    public async Task UpdateStaffDailyWorkModeAsync(string id, DateOnly dutyDate, UpdateStaffDailyWorkModeRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        var businessDate = dutyDate.ToDateTime(TimeOnly.MinValue);
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var staff = await connection.QuerySingleOrDefaultAsync<AdminStaffMemberRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `IS_NOMINATABLE` AS IsNominatable,
                   EXISTS(SELECT 1 FROM ADMIN_USERS U WHERE U.STAFF_MEMBER_ID=STAFF_MEMBERS.ID AND U.IS_ACTIVE=TRUE AND U.ROLE_LEVEL='developer') AS CanUseBackstage
            FROM `STAFF_MEMBERS` WHERE `ID` = @Id FOR UPDATE;
            """, new { Id = id }, transaction, cancellationToken: cancellationToken))
            ?? throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        var plan = await connection.QuerySingleOrDefaultAsync<AdminDutyPlanRow>(new CommandDefinition($"""
            SELECT P.`ID` AS Id, {StaffDutySql.IsOnDuty} AS IsWorking,
                   P.`SCHEDULED_ROLES_JSON` AS ScheduledRolesJson
            FROM `STAFF_DUTY_PLANS` P
            WHERE P.`STAFF_MEMBER_ID` = @Id AND P.`BUSINESS_DATE` = @BusinessDate FOR UPDATE;
            """, new { Id = id, BusinessDate = businessDate, DutyAt = now }, transaction, cancellationToken: cancellationToken));
        var mode = await connection.QuerySingleOrDefaultAsync<AdminStaffMemberRow>(new CommandDefinition("""
            SELECT `ACTIVE_ROLES_JSON` AS ActiveRolesJson, `STOP_ACCEPTING_NEW_ORDERS` AS StopAcceptingNewOrders
            FROM `STAFF_DAILY_WORK_MODES`
            WHERE `STAFF_MEMBER_ID` = @Id AND `BUSINESS_DATE` = @BusinessDate FOR UPDATE;
            """, new { Id = id, BusinessDate = businessDate }, transaction, cancellationToken: cancellationToken));
        var isWorking = plan?.IsWorking == true;
        var scheduled = StaffWorkRoles.ReadSupportRoles(plan?.ScheduledRolesJson);
        var currentRoles = StaffWorkRoles.ReadSupportRoles(mode?.ActiveRolesJson
            ?? (isWorking ? plan?.ScheduledRolesJson : "[]")).ToHashSet(StringComparer.Ordinal);
        var activeRoles = request.ActiveRoles?.ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(currentRoles, StringComparer.Ordinal);
        if (request.SupportRole is { } supportRole)
        {
            if (request.SupportEnabled == true) activeRoles.Add(supportRole);
            else activeRoles.Remove(supportRole);
        }
        if (!activeRoles.SetEquals(currentRoles) && !isWorking)
            throw new ConflictException("目前不在核准班次時段內，無法調整支援工作。", "STAFF_SUPPORT_REQUIRES_DUTY");
        if (!staff.CanUseBackstage && activeRoles.Contains("backstage"))
        {
            if (request.SupportEnabled == true && request.SupportRole == "backstage" || request.ActiveRoles?.Contains("backstage") == true)
                throw new BusinessException("幕後工作僅限開發人員。", "STAFF_BACKSTAGE_NOT_ALLOWED");
            activeRoles.Remove("backstage");
        }
        var allowed = StaffWorkRoles.AllowedSupportRoles(staff.IsNominatable, staff.CanUseBackstage);
        if ((request.SupportEnabled == true && !allowed.Contains(request.SupportRole!, StringComparer.Ordinal))
            || activeRoles.Except(currentRoles).Any(role => !allowed.Contains(role, StringComparer.Ordinal)))
            throw new BusinessException("尚未具備此支援工作的資格，請先確認店員設定。", "STAFF_SUPPORT_NOT_ALLOWED");

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `STAFF_DAILY_WORK_MODES`
                (`ID`, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `SCHEDULED_ROLES_JSON`, `ACTIVE_ROLES_JSON`, `STOP_ACCEPTING_NEW_ORDERS`, `STARTED_AT`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @StaffMemberId, @BusinessDate, @IsWorking, @ScheduledRolesJson, @ActiveRolesJson, @StopAcceptingNewOrders,
                    CASE WHEN @IsWorking = TRUE AND JSON_LENGTH(@ActiveRolesJson) > 0 THEN @Now ELSE NULL END,
                    @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `STOP_ACCEPTING_NEW_ORDERS` = VALUES(`STOP_ACCEPTING_NEW_ORDERS`),
                `ACTIVE_ROLES_JSON` = VALUES(`ACTIVE_ROLES_JSON`),
                `STARTED_AT` = CASE WHEN VALUES(`IS_WORKING`) = TRUE AND JSON_LENGTH(VALUES(`ACTIVE_ROLES_JSON`)) > 0
                                    THEN COALESCE(`STARTED_AT`, @Now) ELSE NULL END,
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new
        {
            Id = NewId(), StaffMemberId = id, IsWorking = isWorking,
            StopAcceptingNewOrders = request.StopAcceptingNewOrders ?? mode?.StopAcceptingNewOrders ?? false,
            ScheduledRolesJson = JsonSerializer.Serialize(scheduled),
            ActiveRolesJson = JsonSerializer.Serialize(activeRoles.Order(StringComparer.Ordinal)),
            BusinessDate = businessDate, Now = now, ActorId = actorId
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdminDutyPlanRow>> GetDutyPlansAsync(DateOnly from, DateOnly to, string? staffId, CancellationToken cancellationToken)
        => QueryAsync<AdminDutyPlanRow>("""
            SELECT P.`ID` AS Id, P.`STAFF_MEMBER_ID` AS StaffId, M.`DISPLAY_NAME` AS StaffName,
                   DATE_FORMAT(P.`BUSINESS_DATE`, '%Y-%m-%d') AS BusinessDate,
                   P.`IS_WORKING` AS IsWorking,
                   CASE WHEN P.`START_TIME` IS NULL THEN NULL ELSE DATE_FORMAT(P.`START_TIME`, '%H:%i') END AS StartTime,
                   CASE WHEN P.`END_TIME` IS NULL THEN NULL ELSE DATE_FORMAT(P.`END_TIME`, '%H:%i') END AS EndTime,
                   P.`SCHEDULED_ROLES_JSON` AS ScheduledRolesJson, P.`PAYROLL_ROLE` AS PayrollRole, P.`APPROVAL_STATUS` AS ApprovalStatus,
                   P.`SUBMITTED_AT` AS SubmittedAt, P.`SUBMITTED_BY` AS SubmittedBy,
                   P.`APPROVED_AT` AS ApprovedAt, P.`APPROVED_BY` AS ApprovedBy,
                   P.`APPROVAL_NOTE` AS ApprovalNote
            FROM `STAFF_DUTY_PLANS` P
            JOIN `STAFF_MEMBERS` M ON M.`ID` = P.`STAFF_MEMBER_ID`
            WHERE P.`BUSINESS_DATE` BETWEEN @FromDate AND @ToDate
              AND (@StaffId IS NULL OR P.`STAFF_MEMBER_ID` = @StaffId)
            ORDER BY P.`BUSINESS_DATE`, M.`SORT_ORDER`, M.`DISPLAY_NAME`;
            """, new
        {
            FromDate = from.ToDateTime(TimeOnly.MinValue),
            ToDate = to.ToDateTime(TimeOnly.MinValue),
            StaffId = staffId
        }, cancellationToken);

    public Task<AdminDutyPlanRow?> GetDutyPlanAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<AdminDutyPlanRow>("""
            SELECT P.`ID` AS Id, P.`STAFF_MEMBER_ID` AS StaffId, M.`DISPLAY_NAME` AS StaffName,
                   DATE_FORMAT(P.`BUSINESS_DATE`, '%Y-%m-%d') AS BusinessDate,
                   P.`IS_WORKING` AS IsWorking,
                   CASE WHEN P.`START_TIME` IS NULL THEN NULL ELSE DATE_FORMAT(P.`START_TIME`, '%H:%i') END AS StartTime,
                   CASE WHEN P.`END_TIME` IS NULL THEN NULL ELSE DATE_FORMAT(P.`END_TIME`, '%H:%i') END AS EndTime,
                   P.`SCHEDULED_ROLES_JSON` AS ScheduledRolesJson, P.`PAYROLL_ROLE` AS PayrollRole, P.`APPROVAL_STATUS` AS ApprovalStatus,
                   P.`SUBMITTED_AT` AS SubmittedAt, P.`SUBMITTED_BY` AS SubmittedBy,
                   P.`APPROVED_AT` AS ApprovedAt, P.`APPROVED_BY` AS ApprovedBy,
                   P.`APPROVAL_NOTE` AS ApprovalNote
            FROM `STAFF_DUTY_PLANS` P
            JOIN `STAFF_MEMBERS` M ON M.`ID` = P.`STAFF_MEMBER_ID`
            WHERE P.`ID` = @Id LIMIT 1;
            """, new { Id = id }, cancellationToken);

    public async Task UpsertDutyPlanAsync(string id, SaveDutyPlanRequest request, string approvalStatus, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        var businessDate = (await _businessDays.GetCurrentAsync(cancellationToken)).BusinessDate.ToDateTime(TimeOnly.MinValue);
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ID FROM STAFF_MEMBERS WHERE ID=@StaffId FOR UPDATE;", new { request.StaffId }, transaction, cancellationToken: cancellationToken));
        var date = DateOnly.ParseExact(request.BusinessDate, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue);
        await PayrollSnapshotStore.CaptureStartedPlanAsync(connection, transaction, request.StaffId, date, now, actorId, cancellationToken);
        var canBackstage = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM ADMIN_USERS WHERE STAFF_MEMBER_ID=@StaffId AND IS_ACTIVE=TRUE AND ROLE_LEVEL='developer');", new { request.StaffId }, transaction, cancellationToken: cancellationToken));
        if (request.IsWorking && !canBackstage && (request.PayrollRole == "backstage" || request.ScheduledRoles.Contains("backstage")))
            throw new BusinessException("幕後工作僅限開發人員。", "DUTY_PLAN_BACKSTAGE_NOT_ALLOWED");
        var values = new
        {
            Id = id,
            request.StaffId,
            BusinessDate = request.BusinessDate,
            request.IsWorking,
            request.StartTime,
            request.EndTime,
            request.PayrollRole,
            InitialPayrollRole = request.PayrollRole ?? StaffWorkRoles.UnambiguousLegacyPayrollRole(request.ScheduledRoles),
            ScheduledRolesJson = JsonSerializer.Serialize(request.ScheduledRoles),
            ApprovalStatus = approvalStatus,
            Now = now,
            ActorId = actorId
        };

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `STAFF_DUTY_PLANS`
                (`ID`, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `START_TIME`, `END_TIME`,
                 `SCHEDULED_ROLES_JSON`, `PAYROLL_ROLE`, `APPROVAL_STATUS`, `SUBMITTED_AT`, `SUBMITTED_BY`,
                 `APPROVED_AT`, `APPROVED_BY`, `APPROVAL_NOTE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @StaffId, @BusinessDate, @IsWorking, @StartTime, @EndTime,
                    @ScheduledRolesJson, @InitialPayrollRole, @ApprovalStatus, @Now, @ActorId,
                    CASE WHEN @ApprovalStatus = 'approved' THEN @Now ELSE NULL END,
                    CASE WHEN @ApprovalStatus = 'approved' THEN @ActorId ELSE NULL END,
                    NULL, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `IS_WORKING` = VALUES(`IS_WORKING`), `START_TIME` = VALUES(`START_TIME`),
                `END_TIME` = VALUES(`END_TIME`), `SCHEDULED_ROLES_JSON` = VALUES(`SCHEDULED_ROLES_JSON`),
                `PAYROLL_ROLE` = CASE WHEN VALUES(`IS_WORKING`) = FALSE THEN NULL
                    ELSE COALESCE(@PayrollRole, `PAYROLL_ROLE`, VALUES(`PAYROLL_ROLE`)) END,
                `APPROVAL_STATUS` = VALUES(`APPROVAL_STATUS`), `SUBMITTED_AT` = VALUES(`SUBMITTED_AT`),
                `SUBMITTED_BY` = VALUES(`SUBMITTED_BY`),
                `APPROVED_AT` = CASE WHEN VALUES(`APPROVAL_STATUS`) = 'approved' THEN VALUES(`SUBMITTED_AT`) ELSE NULL END,
                `APPROVED_BY` = CASE WHEN VALUES(`APPROVAL_STATUS`) = 'approved' THEN VALUES(`SUBMITTED_BY`) ELSE NULL END,
                `APPROVAL_NOTE` = NULL, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, values, transaction, cancellationToken: cancellationToken));

        if (approvalStatus == "approved" && request.BusinessDate == businessDate.ToString("yyyy-MM-dd"))
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_DAILY_WORK_MODES`
                    (`ID`, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `SCHEDULED_ROLES_JSON`,
                     `ACTIVE_ROLES_JSON`, `STARTED_AT`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@ModeId, @StaffId, @BusinessDate, @IsWorking, @ScheduledRolesJson,
                        CASE WHEN @IsWorking = TRUE THEN @ScheduledRolesJson ELSE '[]' END,
                        CASE WHEN @IsWorking = TRUE THEN @Now ELSE NULL END,
                        @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE
                    `IS_WORKING` = VALUES(`IS_WORKING`),
                    `SCHEDULED_ROLES_JSON` = VALUES(`SCHEDULED_ROLES_JSON`),
                    `ACTIVE_ROLES_JSON` = CASE WHEN VALUES(`IS_WORKING`) = FALSE THEN '[]'
                                               ELSE `ACTIVE_ROLES_JSON` END,
                    `STARTED_AT` = CASE WHEN VALUES(`IS_WORKING`) = TRUE THEN COALESCE(`STARTED_AT`, @Now) ELSE NULL END,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
                """, new
            {
                ModeId = NewId(), values.StaffId, values.BusinessDate, values.IsWorking,
                values.ScheduledRolesJson, values.Now, values.ActorId
            }, transaction, cancellationToken: cancellationToken));
        }

        await PayrollSnapshotStore.CaptureStartedPlanAsync(connection, transaction, request.StaffId, date, now, actorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReviewDutyPlanAsync(string id, string action, string? note, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        var businessDate = (await _businessDays.GetCurrentAsync(cancellationToken)).BusinessDate.ToDateTime(TimeOnly.MinValue);
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var staffId = await connection.QuerySingleAsync<string>(new CommandDefinition("SELECT STAFF_MEMBER_ID FROM STAFF_DUTY_PLANS WHERE ID=@Id;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ID FROM STAFF_MEMBERS WHERE ID=@StaffId FOR UPDATE;", new { StaffId = staffId }, transaction, cancellationToken: cancellationToken));
        var currentStatus = await connection.QuerySingleAsync<string>(new CommandDefinition("SELECT APPROVAL_STATUS FROM STAFF_DUTY_PLANS WHERE ID=@Id FOR UPDATE;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (currentStatus is not ("pending" or "rejected")) throw new ConflictException("班表已由其他操作核准或更新，請重新載入。", "DUTY_PLAN_CHANGED");
        var planDate = await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT BUSINESS_DATE FROM STAFF_DUTY_PLANS WHERE ID=@Id FOR UPDATE;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        await PayrollSnapshotStore.CaptureStartedPlanAsync(connection, transaction, staffId, planDate, now, actorId, cancellationToken);
        var invalidBackstage = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT (COALESCE(PAYROLL_ROLE='backstage',FALSE) OR JSON_CONTAINS(SCHEDULED_ROLES_JSON,'"backstage"'))
              AND NOT EXISTS(SELECT 1 FROM ADMIN_USERS WHERE STAFF_MEMBER_ID=@StaffId AND IS_ACTIVE=TRUE AND ROLE_LEVEL='developer')
            FROM STAFF_DUTY_PLANS WHERE ID=@Id;
            """, new { StaffId = staffId, Id = id }, transaction, cancellationToken: cancellationToken));
        if (action == "approve" && invalidBackstage) throw new BusinessException("幕後工作僅限開發人員。", "DUTY_PLAN_BACKSTAGE_NOT_ALLOWED");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `STAFF_DUTY_PLANS`
            SET `APPROVAL_STATUS` = @ApprovalStatus,
                `APPROVED_AT` = CASE WHEN @ApprovalStatus = 'approved' THEN @Now ELSE NULL END,
                `APPROVED_BY` = CASE WHEN @ApprovalStatus = 'approved' THEN @ActorId ELSE NULL END,
                `APPROVAL_NOTE` = @Note, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @Id;
            """, new
        {
            Id = id,
            ApprovalStatus = action == "approve" ? "approved" : "rejected",
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorId = actorId,
            Now = now
        }, transaction, cancellationToken: cancellationToken));

        if (action == "approve")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_DAILY_WORK_MODES`
                    (`ID`, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `SCHEDULED_ROLES_JSON`,
                     `ACTIVE_ROLES_JSON`, `STARTED_AT`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                SELECT @ModeId, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `SCHEDULED_ROLES_JSON`,
                       CASE WHEN `IS_WORKING` = TRUE THEN `SCHEDULED_ROLES_JSON` ELSE '[]' END,
                       CASE WHEN `IS_WORKING` = TRUE THEN @Now ELSE NULL END,
                       @Now, @ActorId, @Now, @ActorId
                FROM `STAFF_DUTY_PLANS`
                WHERE `ID` = @Id AND `BUSINESS_DATE` = @BusinessDate
                ON DUPLICATE KEY UPDATE
                    `IS_WORKING` = VALUES(`IS_WORKING`),
                    `SCHEDULED_ROLES_JSON` = VALUES(`SCHEDULED_ROLES_JSON`),
                    `ACTIVE_ROLES_JSON` = CASE WHEN VALUES(`IS_WORKING`) = FALSE THEN '[]'
                                               ELSE `ACTIVE_ROLES_JSON` END,
                    `STARTED_AT` = CASE WHEN VALUES(`IS_WORKING`) = TRUE THEN COALESCE(`STARTED_AT`, @Now) ELSE NULL END,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
                """, new { Id = id, ModeId = NewId(), ActorId = actorId, BusinessDate = businessDate, Now = now }, transaction, cancellationToken: cancellationToken));
        }

        await PayrollSnapshotStore.CaptureStartedPlanAsync(connection, transaction, staffId, planDate, now, actorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReorderStaffMembersAsync(IReadOnlyList<ReorderStaffMemberItem> items, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in items)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_MEMBERS`
                SET `SORT_ORDER` = @SortOrder, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @Id;
                """, new { item.Id, item.SortOrder, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> StaffMemberHasAdminAccountAsync(string id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM `ADMIN_USERS` WHERE `STAFF_MEMBER_ID` = @Id);";
        return await QuerySingleOrDefaultAsync<bool>(sql, new { Id = id }, cancellationToken);
    }

    public async Task DeleteStaffMemberAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `STAFF_SERVICES` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_GALLERY_ITEMS` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_SCHEDULES` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_DAILY_WORK_MODES` WHERE `STAFF_MEMBER_ID` = @Id; DELETE FROM `STAFF_MEMBERS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdminGalleryAlbumRow>> GetGalleryAlbumsAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminGalleryAlbumRow>("""
            SELECT `ID` AS Id, `ALBUM_TITLE` AS AlbumTitle, `ALBUM_DESCRIPTION` AS AlbumDescription,
                   `COVER_MEDIA_ID` AS CoverMediaId,
                   `PERIOD_TEXT` AS PeriodText, `ENDS_AT` AS EndsAt, `DETAIL_CONTENT` AS DetailContent,
                   `SORT_ORDER` AS SortOrder, `IS_PUBLISHED` AS IsPublished
            FROM `GALLERY_ALBUMS` ORDER BY `SORT_ORDER`, `ALBUM_TITLE`;
            """, null, cancellationToken);

    public Task<AdminGalleryAlbumRow?> GetGalleryAlbumAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<AdminGalleryAlbumRow>("""
            SELECT `ID` AS Id, `ALBUM_TITLE` AS AlbumTitle, `ALBUM_DESCRIPTION` AS AlbumDescription,
                   `COVER_MEDIA_ID` AS CoverMediaId,
                   `PERIOD_TEXT` AS PeriodText, `ENDS_AT` AS EndsAt, `DETAIL_CONTENT` AS DetailContent,
                   `SORT_ORDER` AS SortOrder, `IS_PUBLISHED` AS IsPublished
            FROM `GALLERY_ALBUMS` WHERE `ID` = @Id LIMIT 1;
            """, new { Id = id }, cancellationToken);

    public Task<IReadOnlyList<AdminGalleryItemRow>> GetGalleryItemsAsync(string albumId, CancellationToken cancellationToken)
        => QueryAsync<AdminGalleryItemRow>("""
            SELECT `ID` AS Id, `ALBUM_ID` AS AlbumId, `MEDIA_ID` AS MediaId,
                   `TITLE` AS Title,
                   `CAPTION` AS Caption, `SHOT_AT` AS ShotAt, `SORT_ORDER` AS SortOrder,
                   `IS_PUBLISHED` AS IsPublished
            FROM `GALLERY_ITEMS` WHERE `ALBUM_ID` = @AlbumId
            ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, new { AlbumId = albumId }, cancellationToken);

    public async Task UpsertGalleryAlbumAsync(string id, SaveGalleryAlbumRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `GALLERY_ALBUMS`
                (`ID`, `ALBUM_TITLE`, `ALBUM_DESCRIPTION`, `COVER_MEDIA_ID`, `PERIOD_TEXT`, `ENDS_AT`, `DETAIL_CONTENT`, `SORT_ORDER`, `IS_PUBLISHED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @AlbumTitle, @AlbumDescription, @CoverMediaId, @PeriodText, @EndsAt, @DetailContent, @SortOrder, @IsPublished, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `ALBUM_TITLE` = VALUES(`ALBUM_TITLE`), `ALBUM_DESCRIPTION` = VALUES(`ALBUM_DESCRIPTION`),
                `COVER_MEDIA_ID` = VALUES(`COVER_MEDIA_ID`),
                `PERIOD_TEXT` = VALUES(`PERIOD_TEXT`), `ENDS_AT` = VALUES(`ENDS_AT`), `DETAIL_CONTENT` = VALUES(`DETAIL_CONTENT`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_PUBLISHED` = VALUES(`IS_PUBLISHED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.AlbumTitle, request.AlbumDescription, request.CoverMediaId,
                request.PeriodText, request.EndsAt, request.DetailContent, request.SortOrder, request.IsPublished,
                Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `GALLERY_ITEMS` WHERE `ALBUM_ID` = @AlbumId;",
            new { AlbumId = id }, transaction, cancellationToken: cancellationToken));
        foreach (var item in request.Items)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `GALLERY_ITEMS`
                    (`ID`, `ALBUM_ID`, `MEDIA_ID`, `TITLE`, `CAPTION`, `SHOT_AT`, `SORT_ORDER`, `IS_PUBLISHED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @AlbumId, @MediaId, @Title, @Caption, @ShotAt, @SortOrder, @IsPublished, @Now, @ActorId, @Now, @ActorId);
                """, new { Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id, AlbumId = id, item.MediaId,
                    item.Title, item.Caption, item.ShotAt, item.SortOrder,
                    item.IsPublished, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteGalleryAlbumAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `GALLERY_ITEMS` WHERE `ALBUM_ID` = @Id; DELETE FROM `HOME_EVENT_CAROUSELS` WHERE `ALBUM_ID` = @Id; DELETE FROM `GALLERY_ALBUMS` WHERE `ID` = @Id;",
            new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdminPricingRuleRow>> GetPricingRulesAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminPricingRuleRow>("""
            SELECT `ID` AS Id, `TITLE` AS Title, `DESCRIPTION` AS Description, `PRICE_TEXT` AS PriceText, `POLICY_JSON` AS PolicyJson,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `PRICING_RULES` ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, null, cancellationToken);

    public Task UpsertPricingRuleAsync(string id, SavePricingRuleRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `PRICING_RULES`
                (`ID`, `TITLE`, `DESCRIPTION`, `PRICE_TEXT`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`, `POLICY_JSON`)
            VALUES (@Id, @Title, @Description, @PriceText, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId, @PolicyJson)
            ON DUPLICATE KEY UPDATE `TITLE` = VALUES(`TITLE`), `DESCRIPTION` = VALUES(`DESCRIPTION`), `PRICE_TEXT` = VALUES(`PRICE_TEXT`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_ENABLED` = VALUES(`IS_ENABLED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId, `POLICY_JSON` = COALESCE(@PolicyJson, `POLICY_JSON`);
            """, new { Id = id, PolicyJson = request.Policy is null ? null : JsonSerializer.Serialize(request.Policy), request.Title, request.Description, request.PriceText, request.SortOrder, request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeletePricingRuleAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `PRICING_RULES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public async Task<(IReadOnlyList<AdminMenuCategoryRow> Categories, IReadOnlyList<AdminMenuItemRow> Items, IReadOnlyList<AdminMenuSetRow> Sets, IReadOnlyList<AdminMenuSetItemRow> SetItems)> GetMenuAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `CATEGORY_NAME` AS CategoryName, `CATEGORY_DESCRIPTION` AS CategoryDescription,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `MENU_CATEGORIES` ORDER BY `SORT_ORDER`, `CATEGORY_NAME`;
            SELECT `ID` AS Id, `CATEGORY_ID` AS CategoryId, `ITEM_NAME` AS ItemName, `GAME_ITEM_NAME` AS GameItemName, `ITEM_DESCRIPTION` AS ItemDescription,
                   `PRICE` AS Price, `MEDIA_ID` AS MediaId, `TAGS` AS Tags, `POLICY_JSON` AS PolicyJson,
                   `SORT_ORDER` AS SortOrder, `IS_AVAILABLE` AS IsAvailable
            FROM `MENU_ITEMS` ORDER BY `CATEGORY_ID`, `SORT_ORDER`, `ITEM_NAME`;
            SELECT `ID` AS Id, `SET_NAME` AS SetName, `SET_DESCRIPTION` AS SetDescription, `SET_PRICE` AS SetPrice, `POLICY_JSON` AS PolicyJson,
                   `MEDIA_ID` AS MediaId, `SORT_ORDER` AS SortOrder, `IS_AVAILABLE` AS IsAvailable
            FROM `MENU_SETS` ORDER BY `SORT_ORDER`, `SET_NAME`;
            SELECT SI.`ID` AS Id, SI.`SET_ID` AS SetId, SI.`MENU_ITEM_ID` AS MenuItemId,
                   I.`ITEM_NAME` AS ItemName, SI.`ITEM_ROLE` AS ItemRole, SI.`QUANTITY` AS Quantity, SI.`SORT_ORDER` AS SortOrder
            FROM `MENU_SET_ITEMS` SI LEFT JOIN `MENU_ITEMS` I ON I.`ID` = SI.`MENU_ITEM_ID`
            ORDER BY SI.`SET_ID`, SI.`SORT_ORDER`;
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return ((await multi.ReadAsync<AdminMenuCategoryRow>()).AsList(),
            (await multi.ReadAsync<AdminMenuItemRow>()).AsList(),
            (await multi.ReadAsync<AdminMenuSetRow>()).AsList(),
            (await multi.ReadAsync<AdminMenuSetItemRow>()).AsList());
    }

    public Task UpsertMenuCategoryAsync(string id, SaveMenuCategoryRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `MENU_CATEGORIES`
                (`ID`, `CATEGORY_NAME`, `CATEGORY_DESCRIPTION`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @CategoryName, @CategoryDescription, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `CATEGORY_NAME` = VALUES(`CATEGORY_NAME`), `CATEGORY_DESCRIPTION` = VALUES(`CATEGORY_DESCRIPTION`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_ENABLED` = VALUES(`IS_ENABLED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.CategoryName, request.CategoryDescription, request.SortOrder, request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public async Task DeleteMenuCategoryAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT ID FROM MENU_CATEGORIES WHERE ID=@Id FOR UPDATE;",new{Id=id},transaction,cancellationToken:cancellationToken));
        var hasItems = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*) > 0 FROM `MENU_ITEMS` WHERE `CATEGORY_ID` = @Id;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (hasItems) throw new BusinessException("此分類仍有餐點，請先移除或移至其他分類。","MENU_CATEGORY_IN_USE");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_CATEGORIES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertMenuItemAsync(string id, SaveMenuItemRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var category = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT ID FROM MENU_CATEGORIES WHERE ID=@Id FOR UPDATE;",new{Id=request.CategoryId},transaction,cancellationToken:cancellationToken));
        if(category is null) throw new BusinessException("所屬分類已移除，請重新載入。","MENU_CATEGORY_INVALID");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `MENU_ITEMS`
                (`ID`, `CATEGORY_ID`, `ITEM_NAME`, `GAME_ITEM_NAME`, `ITEM_DESCRIPTION`, `PRICE`, `MEDIA_ID`, `TAGS`, `SORT_ORDER`, `IS_AVAILABLE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`, `POLICY_JSON`)
            VALUES (@Id, @CategoryId, @ItemName, NULLIF(TRIM(@GameItemName), ''), @ItemDescription, @Price, @MediaId, @Tags, @SortOrder, @IsAvailable, @Now, @ActorId, @Now, @ActorId, @PolicyJson)
            ON DUPLICATE KEY UPDATE `CATEGORY_ID` = VALUES(`CATEGORY_ID`), `ITEM_NAME` = VALUES(`ITEM_NAME`),
                `GAME_ITEM_NAME` = CASE WHEN @GameItemName IS NULL THEN `GAME_ITEM_NAME` ELSE NULLIF(TRIM(@GameItemName), '') END,
                `ITEM_DESCRIPTION` = VALUES(`ITEM_DESCRIPTION`), `PRICE` = VALUES(`PRICE`), `MEDIA_ID` = VALUES(`MEDIA_ID`),
                `TAGS` = VALUES(`TAGS`), `SORT_ORDER` = VALUES(`SORT_ORDER`),
                `IS_AVAILABLE` = VALUES(`IS_AVAILABLE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId, `POLICY_JSON` = COALESCE(@PolicyJson, `POLICY_JSON`);
            """, new { Id = id, PolicyJson = request.Policy is null ? null : JsonSerializer.Serialize(request.Policy), request.CategoryId, request.ItemName, request.GameItemName, request.ItemDescription, request.Price,
                request.MediaId, Tags = request.Tags?.GetRawText(), request.SortOrder,
                request.IsAvailable, Now = now, ActorId = actorId }, transaction, cancellationToken:cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteMenuItemAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT ID FROM MENU_ITEMS WHERE ID=@Id FOR UPDATE;",new{Id=id},transaction,cancellationToken:cancellationToken));
        var used = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*) > 0 FROM `MENU_SET_ITEMS` WHERE `MENU_ITEM_ID` = @Id;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (used) throw new BusinessException("此餐點仍用於套餐，請先移除套餐內容或改為停售。","MENU_ITEM_IN_USE");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_ITEMS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveMenuSetAsync(string id, SaveMenuSetRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existingItems=(await connection.QueryAsync<string>(new CommandDefinition("SELECT ID FROM MENU_ITEMS ORDER BY ID FOR UPDATE;",transaction:transaction,cancellationToken:cancellationToken))).ToHashSet(StringComparer.Ordinal);
        if(request.Items.Any(item=>!existingItems.Contains(item.MenuItemId))) throw new BusinessException("套餐內有已移除餐點，請重新載入。","MENU_SET_ITEM_INVALID");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `MENU_SETS`
                (`ID`, `SET_NAME`, `SET_DESCRIPTION`, `SET_PRICE`, `MEDIA_ID`, `SORT_ORDER`, `IS_AVAILABLE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`, `POLICY_JSON`)
            VALUES (@Id, @SetName, @SetDescription, @SetPrice, @MediaId, @SortOrder, @IsAvailable, @Now, @ActorId, @Now, @ActorId, @PolicyJson)
            ON DUPLICATE KEY UPDATE `SET_NAME` = VALUES(`SET_NAME`), `SET_DESCRIPTION` = VALUES(`SET_DESCRIPTION`),
                `SET_PRICE` = VALUES(`SET_PRICE`), `MEDIA_ID` = VALUES(`MEDIA_ID`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_AVAILABLE` = VALUES(`IS_AVAILABLE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId, `POLICY_JSON` = COALESCE(@PolicyJson, `POLICY_JSON`);
            """, new { Id = id, PolicyJson = request.Policy is null ? null : JsonSerializer.Serialize(request.Policy), request.SetName, request.SetDescription, request.SetPrice, request.MediaId,
                request.SortOrder, request.IsAvailable, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_SET_ITEMS` WHERE `SET_ID` = @SetId;", new { SetId = id }, transaction, cancellationToken: cancellationToken));
        foreach (var item in request.Items)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `MENU_SET_ITEMS`
                    (`ID`, `SET_ID`, `MENU_ITEM_ID`, `ITEM_ROLE`, `QUANTITY`, `SORT_ORDER`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @SetId, @MenuItemId, @ItemRole, @Quantity, @SortOrder, @Now, @ActorId, @Now, @ActorId);
                """, new { Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id, SetId = id,
                    item.MenuItemId, item.ItemRole, item.Quantity, item.SortOrder, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteMenuSetAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_SET_ITEMS` WHERE `SET_ID` = @Id; DELETE FROM `MENU_SETS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    private static string NewId() => Guid.NewGuid().ToString("D");
}
