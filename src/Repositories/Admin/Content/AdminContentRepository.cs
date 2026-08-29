using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Content;

public sealed class AdminContentRepository : DapperRepositoryBase, IAdminContentRepository
{
    public AdminContentRepository(AppDbContext dbContext) : base(dbContext)
    {
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

    public Task<IReadOnlyList<AdminStaffMemberListRow>> GetStaffMembersAsync(CancellationToken cancellationToken)
        => QueryAsync<AdminStaffMemberListRow>("""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`ROLE_TITLE` AS RoleTitle,
                   COALESCE(S.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   M.`BUFFER_MINUTES` AS BufferMinutes, M.`IS_NOMINATABLE` AS IsNominatable,
                   M.`SORT_ORDER` AS SortOrder, M.`IS_ACTIVE` AS IsActive
            FROM `STAFF_MEMBERS` M
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID` = M.`ID` AND S.`WORK_DATE` = CURRENT_DATE()
            ORDER BY `SORT_ORDER`, `DISPLAY_NAME`;
            """, null, cancellationToken);

    public Task<AdminStaffMemberRow?> GetStaffMemberAsync(string id, CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<AdminStaffMemberRow>("""
            SELECT M.`ID` AS Id, M.`DISPLAY_NAME` AS DisplayName, M.`NICKNAME` AS Nickname,
                   M.`AVATAR_MEDIA_ID` AS AvatarMediaId, M.`ROLE_TITLE` AS RoleTitle,
                   M.`SHORT_BIO` AS ShortBio, M.`PROFILE_BIO` AS ProfileBio,
                   COALESCE(S.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN 'off'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN 'busy'
                        ELSE 'available' END AS CurrentStatus,
                   CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = FALSE THEN '未上班'
                        WHEN EXISTS (SELECT 1 FROM `STAFF_RESERVATIONS` R WHERE R.`STAFF_ID` = M.`ID` AND R.`RESERVATION_STATUS` = 'active' AND R.`STARTS_AT` <= NOW() AND R.`ENDS_AT` > NOW()) THEN '指名中'
                        ELSE '待命中' END AS StatusText,
                   NULL AS TodayShift, M.`BUFFER_MINUTES` AS BufferMinutes,
                   M.`IS_NOMINATABLE` AS IsNominatable,
                   M.`SORT_ORDER` AS SortOrder, M.`IS_ACTIVE` AS IsActive
            FROM `STAFF_MEMBERS` M
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID` = M.`ID` AND S.`WORK_DATE` = CURRENT_DATE()
            WHERE M.`ID` = @Id LIMIT 1;
            """, new { Id = id }, cancellationToken);

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
                `AVATAR_MEDIA_ID` = @AvatarMediaId,
                `ROLE_TITLE` = @RoleTitle, `SHORT_BIO` = @ShortBio, `PROFILE_BIO` = @ProfileBio,
                `BUFFER_MINUTES` = @BufferMinutes, `IS_NOMINATABLE` = @IsNominatable,
                `SORT_ORDER` = @SortOrder,
                `IS_ACTIVE` = @IsActive, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @Id;
            """, new { Id = id, request.DisplayName, request.Nickname, request.AvatarMediaId,
                request.RoleTitle, request.ShortBio, request.ProfileBio, request.BufferMinutes, request.IsNominatable,
                request.SortOrder, request.IsActive, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `STAFF_SCHEDULES`
                (`ID`, `STAFF_ID`, `WORK_DATE`, `IS_WORKING`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@ScheduleId, @StaffId, DATE(@Now), @IsWorkingToday, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `IS_WORKING` = VALUES(`IS_WORKING`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { ScheduleId = NewId(), StaffId = id, request.IsWorkingToday, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));

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

    public async Task UpdateStaffMemberStatusAsync(string id, bool? isWorkingToday, bool? isActive, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (isActive.HasValue)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_MEMBERS`
                SET `IS_ACTIVE` = @IsActive, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @Id;
                """, new { Id = id, IsActive = isActive.Value, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }

        if (isWorkingToday.HasValue)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_SCHEDULES`
                    (`ID`, `STAFF_ID`, `WORK_DATE`, `IS_WORKING`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@ScheduleId, @StaffId, DATE(@Now), @IsWorkingToday, @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE `IS_WORKING` = VALUES(`IS_WORKING`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
                """, new { ScheduleId = NewId(), StaffId = id, IsWorkingToday = isWorkingToday.Value, Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));
        }

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
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `STAFF_SERVICES` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_GALLERY_ITEMS` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_SCHEDULES` WHERE `STAFF_ID` = @Id; DELETE FROM `STAFF_MEMBERS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
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
            SELECT `ID` AS Id, `TITLE` AS Title, `DESCRIPTION` AS Description, `PRICE_TEXT` AS PriceText,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `PRICING_RULES` ORDER BY `SORT_ORDER`, `CREATED_AT`;
            """, null, cancellationToken);

    public Task UpsertPricingRuleAsync(string id, SavePricingRuleRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `PRICING_RULES`
                (`ID`, `TITLE`, `DESCRIPTION`, `PRICE_TEXT`, `SORT_ORDER`, `IS_ENABLED`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @Title, @Description, @PriceText, @SortOrder, @IsEnabled, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `TITLE` = VALUES(`TITLE`), `DESCRIPTION` = VALUES(`DESCRIPTION`), `PRICE_TEXT` = VALUES(`PRICE_TEXT`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_ENABLED` = VALUES(`IS_ENABLED`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.Title, request.Description, request.PriceText, request.SortOrder, request.IsEnabled, Now = now, ActorId = actorId }, cancellationToken);

    public Task DeletePricingRuleAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("DELETE FROM `PRICING_RULES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken);

    public async Task<(IReadOnlyList<AdminMenuCategoryRow> Categories, IReadOnlyList<AdminMenuItemRow> Items, IReadOnlyList<AdminMenuSetRow> Sets, IReadOnlyList<AdminMenuSetItemRow> SetItems)> GetMenuAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `CATEGORY_NAME` AS CategoryName, `CATEGORY_DESCRIPTION` AS CategoryDescription,
                   `SORT_ORDER` AS SortOrder, `IS_ENABLED` AS IsEnabled
            FROM `MENU_CATEGORIES` ORDER BY `SORT_ORDER`, `CATEGORY_NAME`;
            SELECT `ID` AS Id, `CATEGORY_ID` AS CategoryId, `ITEM_NAME` AS ItemName, `ITEM_DESCRIPTION` AS ItemDescription,
                   `PRICE` AS Price, `MEDIA_ID` AS MediaId, `TAGS` AS Tags,
                   `SORT_ORDER` AS SortOrder, `IS_AVAILABLE` AS IsAvailable
            FROM `MENU_ITEMS` ORDER BY `CATEGORY_ID`, `SORT_ORDER`, `ITEM_NAME`;
            SELECT `ID` AS Id, `SET_NAME` AS SetName, `SET_DESCRIPTION` AS SetDescription, `SET_PRICE` AS SetPrice,
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
        var hasItems = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*) > 0 FROM `MENU_ITEMS` WHERE `CATEGORY_ID` = @Id;", new { Id = id }, cancellationToken: cancellationToken));
        if (hasItems) throw new InvalidOperationException("Menu category still contains items.");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_CATEGORIES` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken: cancellationToken));
    }

    public Task UpsertMenuItemAsync(string id, SaveMenuItemRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
        => ExecuteAsync("""
            INSERT INTO `MENU_ITEMS`
                (`ID`, `CATEGORY_ID`, `ITEM_NAME`, `ITEM_DESCRIPTION`, `PRICE`, `MEDIA_ID`, `TAGS`, `SORT_ORDER`, `IS_AVAILABLE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @CategoryId, @ItemName, @ItemDescription, @Price, @MediaId, @Tags, @SortOrder, @IsAvailable, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `CATEGORY_ID` = VALUES(`CATEGORY_ID`), `ITEM_NAME` = VALUES(`ITEM_NAME`),
                `ITEM_DESCRIPTION` = VALUES(`ITEM_DESCRIPTION`), `PRICE` = VALUES(`PRICE`), `MEDIA_ID` = VALUES(`MEDIA_ID`),
                `TAGS` = VALUES(`TAGS`), `SORT_ORDER` = VALUES(`SORT_ORDER`),
                `IS_AVAILABLE` = VALUES(`IS_AVAILABLE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.CategoryId, request.ItemName, request.ItemDescription, request.Price,
                request.MediaId, Tags = request.Tags?.GetRawText(), request.SortOrder,
                request.IsAvailable, Now = now, ActorId = actorId }, cancellationToken);

    public async Task DeleteMenuItemAsync(string id, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var used = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*) > 0 FROM `MENU_SET_ITEMS` WHERE `MENU_ITEM_ID` = @Id;", new { Id = id }, cancellationToken: cancellationToken));
        if (used) throw new InvalidOperationException("Menu item is used by a menu set.");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `MENU_ITEMS` WHERE `ID` = @Id;", new { Id = id, ActorId = actorId, Now = now }, cancellationToken: cancellationToken));
    }

    public async Task SaveMenuSetAsync(string id, SaveMenuSetRequest request, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `MENU_SETS`
                (`ID`, `SET_NAME`, `SET_DESCRIPTION`, `SET_PRICE`, `MEDIA_ID`, `SORT_ORDER`, `IS_AVAILABLE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @SetName, @SetDescription, @SetPrice, @MediaId, @SortOrder, @IsAvailable, @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `SET_NAME` = VALUES(`SET_NAME`), `SET_DESCRIPTION` = VALUES(`SET_DESCRIPTION`),
                `SET_PRICE` = VALUES(`SET_PRICE`), `MEDIA_ID` = VALUES(`MEDIA_ID`),
                `SORT_ORDER` = VALUES(`SORT_ORDER`), `IS_AVAILABLE` = VALUES(`IS_AVAILABLE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
            """, new { Id = id, request.SetName, request.SetDescription, request.SetPrice, request.MediaId,
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
