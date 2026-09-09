using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Menu;

public sealed record MenuNotificationRule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string RuleType { get; init; } = "order_received";
    public bool IsEnabled { get; init; } = true;
    public string PopupMode { get; init; } = "toast";
    public string? SoundId { get; init; }
    public string AudienceMode { get; init; } = "all";
    public string[] AudienceStaffIds { get; init; } = [];
    public string[] AudienceRoles { get; init; } = [];
    public string Priority { get; init; } = "normal";
    public int ExpiresAfterMinutes { get; init; } = 15;
    public string? TemplateCode { get; init; }
    public bool RequiresAck { get; init; }
    public int RepeatIntervalMinutes { get; init; }
    public int MaxOccurrences { get; init; } = 1;
    public int BacklogThreshold { get; init; } = 1;
    public int BacklogDurationMinutes { get; init; } = 5;
    public int SchemaVersion { get; init; }
    public long RuleRevision { get; init; }
    public string Fingerprint { get; init; } = "";
    public string? TargetMode { get; init; }
    public string? TargetStaffId { get; init; }
    public int? OffsetMinutes { get; init; }
}
public sealed record MenuNotificationSettings(long Revision, IReadOnlyList<MenuNotificationRule> Rules)
{
    public int SchemaVersion { get; init; } = 2;
}
public sealed class SaveMenuNotificationsRequest
{
    [Range(0,long.MaxValue)] public long ExpectedRevision { get; init; }
    public int SchemaVersion { get; init; }
    public MenuNotificationRule[] Rules { get; init; } = [];
}
public sealed class SendBroadcastRequest
{
    [Required, StringLength(80, MinimumLength = 8)] public string IdempotencyKey { get; init; } = "";
    [Required, StringLength(120, MinimumLength = 1)] public string Title { get; init; } = "";
    [Required, StringLength(2000, MinimumLength = 1)] public string Message { get; init; } = "";
    public string AudienceMode { get; init; } = "all";
    public string[] AudienceStaffIds { get; init; } = [];
    public string[] AudienceRoles { get; init; } = [];
    public string Priority { get; init; } = "normal";
    [Range(1, 1440)] public int ExpiresAfterMinutes { get; init; } = 15;
    [Required] public string? SoundId { get; init; }
    public string Action { get; init; } = "open_notifications";
    public bool RequiresAck { get; init; }
    public bool Emergency { get; init; }
    [StringLength(500)] public string? Reason { get; init; }
}
public sealed record BroadcastSendResult(string BroadcastId, string SourceKey, bool AlreadySent, int RecipientCount);
public sealed record BroadcastWithdrawResult(string BroadcastId, bool AlreadyWithdrawn, int AffectedDeliveryCount);
public sealed record NotificationAcknowledgementResult(string DeliveryId, bool AlreadyAcknowledged, int AcknowledgedMatchCount);
public sealed record BroadcastReceiptSummary(string BroadcastId, int RecipientCount, int UnreadCount,
    int ReadCount, int AcknowledgedCount, int PendingAcknowledgementCount,
    int ExpiredUnacknowledgedCount, int WithdrawnCount);
public sealed record MenuNotificationMessage(string? OrderId, string Title, string Message,
    string PopupMode, string? SoundId, bool IsBroadcast, string[] MatchedRules)
{
    public int PayloadVersion { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public string? Action { get; init; }
    public string Priority { get; init; } = "normal";
    public int ExpiresAfterMinutes { get; init; } = 15;
    public bool RequiresAck { get; init; }
    public int OccurrenceNo { get; init; } = 1;
    public string? GroupKey { get; init; }
    public string? EpisodeId { get; init; }
    public string? PresentationId { get; init; }
    public MenuNotificationRuleMatch[] RuleMatches { get; init; } = [];
}
public sealed record MenuNotificationRuleMatch(string OwnerKey, string RuleId, string RuleType,
    long RuleRevision, string Fingerprint, string PopupMode, string? SoundId, bool IsBroadcast)
{
    public string Priority { get; init; } = "normal";
    public int ExpiresAfterMinutes { get; init; } = 15;
    public bool RequiresAck { get; init; }
    public int RepeatIntervalMinutes { get; init; }
    public int MaxOccurrences { get; init; } = 1;
}
public sealed record MenuNotificationDelivery(string Id, MenuNotificationMessage Content,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ReadAt,
    DateTimeOffset? AcknowledgedAt, DateTimeOffset? WithdrawnAt);
public sealed record MenuNotificationInbox(IReadOnlyList<MenuNotificationDelivery> Items, int UnreadCount)
{
    public string SnapshotCursor { get; init; } = "";
    public string? NextPageToken { get; init; }
    public bool HasMore { get; init; }
}
public sealed record MenuNotificationChange(long Sequence,string ChangeId,string ChangeType,string? DeliveryId,
    MenuNotificationDelivery? Delivery,DateTimeOffset CreatedAt);
public sealed record MenuNotificationChangePage(IReadOnlyList<MenuNotificationChange> Items,string Cursor,
    bool ResyncRequired,string? ResyncReason);

public sealed class MenuNotifications(AppDbContext db, IAppClock clock, NotificationSounds sounds, ILogger<MenuNotifications> logger)
{
    private const int NotificationSchemaVersion = 2;
    private static readonly string[] SupportedRuleTypes = [
        "order_received", "designated_order_received", "nomination_starting", "nomination_ending",
        "nomination_ended", "business_opening_soon", "business_closing_soon", "champagne_order_received",
        "order_backlog"];
    private static readonly string[] BroadcastRuleTypes = ["order_received", "champagne_order_received", "order_backlog"];
    private static readonly string[] BroadcastAudienceModes = ["all", "working_today", "roles", "staff"];
    private static readonly string[] BroadcastPriorities = ["normal", "high"];
    private static readonly string[] BroadcastRoles = ["service", "designated", "backstage"];

    public static string Account(ClaimsPrincipal user) => user.FindFirst(AdminAuthConstants.UserIdClaimType)?.Value ?? throw new UnauthorizedAccessException();
    private static string Owner(ClaimsPrincipal user, bool broadcast)
    {
        if (broadcast)
        {
            if (user.FindFirst(AdminAuthConstants.RoleClaimType)?.Value is not ("manager" or "developer")) throw new ForbiddenException("僅管理者可管理店內廣播。");
            return "broadcast";
        }
        var staff = user.FindFirst(AdminAuthConstants.StaffMemberIdClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(staff)) throw new BusinessException("個人規則需要關聯店員身分。", "NOTIFICATION_STAFF_REQUIRED");
        return "staff:" + staff;
    }
    public async Task<MenuNotificationSettings> SettingsAsync(ClaimsPrincipal user, bool broadcast, CancellationToken ct)
    {
        var owner = Owner(user,broadcast);
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY=@Owner;",new {Owner=owner},cancellationToken:ct));
        var document = row is null ? new NotificationSettingsDocument(NotificationSchemaVersion, []) : ReadSettingsDocument(row.RulesJson);
        return new(row?.Revision??0, document.Rules.Select(rule=>NormalizeStoredRule(rule, user.FindFirst(AdminAuthConstants.StaffMemberIdClaimType)?.Value)).ToArray())
        {
            SchemaVersion = NotificationSchemaVersion,
        };
    }
    public async Task<MenuNotificationSettings> SaveAsync(ClaimsPrincipal user, bool broadcast, SaveMenuNotificationsRequest request, CancellationToken ct)
    {
        var owner = Owner(user,broadcast); var actor = Account(user);
        var ownerStaffId = user.FindFirst(AdminAuthConstants.StaffMemberIdClaimType)?.Value;
        if (request.Rules is null || request.Rules.Length>20 || request.Rules.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=request.Rules.Length)
            throw new BusinessException("通知規則最多 20 條，且每條規則識別碼不可重複。", "NOTIFICATION_RULE_DUPLICATED");
        var normalizedRules = request.Rules.Select(rule=>NormalizeRule(rule,ownerStaffId)).ToArray();
        foreach(var rule in normalizedRules)
        {
            if(!Guid.TryParse(rule.Id,out _) || !SupportedRuleTypes.Contains(rule.RuleType,StringComparer.Ordinal) ||
                (broadcast && !BroadcastRuleTypes.Contains(rule.RuleType,StringComparer.Ordinal)) ||
                (!broadcast && rule.RuleType=="order_backlog") ||
                (!broadcast && (rule.RepeatIntervalMinutes!=0 || rule.MaxOccurrences!=1 || rule.BacklogThreshold!=1 || rule.BacklogDurationMinutes!=5)) ||
                (broadcast ? rule.PopupMode is not ("banner" or "critical_modal") : rule.PopupMode is not ("none" or "toast" or "sticky")))
                throw new BusinessException("通知規則參數無效。", "NOTIFICATION_RULE_INVALID");
            ValidateRuleConditions(rule);
            if(broadcast) ValidateBroadcastRule(rule);
            if(broadcast && rule.IsEnabled && rule.SoundId is null)
                throw new BusinessException("店內廣播啟用前必須選擇有效系統音效。", "BROADCAST_SOUND_REQUIRED");
        }
        if(normalizedRules.GroupBy(x=>x.Fingerprint,StringComparer.Ordinal).Any(x=>x.Count()>1))
            throw new BusinessException("相同觸發條件只能設定一次；不同 N 或監看對象可並存。", "NOTIFICATION_RULE_DUPLICATED");
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO NOTIFICATION_SETTINGS (OWNER_KEY,REVISION,RULES_JSON,UPDATED_AT,UPDATED_BY) VALUES (@Owner,0,'[]',@Now,@Actor);",new {Owner=owner,Now=clock.LocalDateTime,Actor=actor},tx,cancellationToken:ct));
        var row=await connection.QuerySingleAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY=@Owner FOR UPDATE;",new {Owner=owner},tx,cancellationToken:ct));
        if(row.Revision!=request.ExpectedRevision) throw new ConflictException("通知設定已變更，請重新載入比對草稿。","NOTIFICATION_REVISION_CONFLICT");
        var current = ReadSettingsDocument(row.RulesJson);
        var requestHasV2Fields = request.SchemaVersion>=NotificationSchemaVersion || request.Rules.Any(HasVersionedFields);
        if(current.SchemaVersion>=NotificationSchemaVersion && !requestHasV2Fields)
            throw new BusinessException("通知設定已使用新版規則格式，請重新載入後再儲存。", "NOTIFICATION_SCHEMA_UPGRADE_REQUIRED");
        var staffTargets=normalizedRules.Where(x=>x.TargetMode=="staff" && x.TargetStaffId is not null).Select(x=>x.TargetStaffId!).Concat(
            normalizedRules.Where(x=>x.AudienceMode=="staff").SelectMany(x=>x.AudienceStaffIds)).Distinct(StringComparer.Ordinal).ToArray();
        if(staffTargets.Length>0)
        {
            var existing=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM STAFF_MEMBERS WHERE IS_ACTIVE=TRUE AND ID IN @Ids;",new{Ids=staffTargets},tx,cancellationToken:ct));
            if(existing!=staffTargets.Length) throw new BusinessException("指定的店員不存在或目前未啟用。", "NOTIFICATION_TARGET_STAFF_INVALID");
        }
        var previousById=current.Rules.Select(rule=>NormalizeStoredRule(rule,ownerStaffId)).ToDictionary(x=>x.Id,StringComparer.Ordinal);
        var rules=normalizedRules.Select(rule=>
        {
            if(previousById.TryGetValue(rule.Id,out var previous) && previous.Fingerprint==rule.Fingerprint && previous.IsEnabled==rule.IsEnabled)
                return rule with {RuleRevision=previous.RuleRevision<=0?1:previous.RuleRevision,SchemaVersion=NotificationSchemaVersion};
            return rule with {RuleRevision=previousById.TryGetValue(rule.Id,out previous)?Math.Max(1,previous.RuleRevision+1):1,SchemaVersion=NotificationSchemaVersion};
        }).ToArray();
        foreach(var rule in rules.Where(x=>x.SoundId is not null))
        {
            await NotificationSounds.ValidateAccessAsync(connection,tx,rule.SoundId!,user,broadcast,ct);
            _ = await sounds.ContentAsync(user,rule.SoundId!,ct);
        }
        var json=JsonSerializer.Serialize(new NotificationSettingsDocument(NotificationSchemaVersion,rules),MenuPolicies.Json);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_SETTINGS SET REVISION=REVISION+1,RULES_JSON=@Json,UPDATED_AT=@Now,UPDATED_BY=@Actor WHERE OWNER_KEY=@Owner;",new {Owner=owner,Json=json,Now=clock.LocalDateTime,Actor=actor},tx,cancellationToken:ct));
        if(!broadcast)
        {
            var futureNominees=(await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
                SELECT N.`ID` AS Id, N.`ORDER_ID` AS OrderId, N.`STAFF_ID` AS StaffId,
                       N.`REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       N.`REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       N.`REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       N.`CONFIRMATION_STATUS` AS ConfirmationStatus
                FROM `ORDER_NOMINEES` N
                JOIN `ORDERS` O ON O.`ID`=N.`ORDER_ID`
                WHERE N.`CONFIRMATION_STATUS`='confirmed'
                  AND O.`ORDER_STATUS` IN ('confirmed','in_service')
                  AND N.`REQUESTED_SERVICE_ENDS_AT`>=@Now;
                """,new{Now=clock.LocalDateTime},tx,cancellationToken:ct))).AsList();
            await EnsureNominationSchedulesAsync(connection,tx,futureNominees,clock.LocalDateTime,ct);
        }
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@Id,@Actor,'settings.updated',@Owner,@Before,@After,@Now);",new {Id=Guid.NewGuid().ToString(),Actor=actor,Owner=owner,Before=row.RulesJson,After=json,Now=clock.LocalDateTime},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(row.Revision+1,rules){SchemaVersion=NotificationSchemaVersion};
    }

    public async Task<BroadcastSendResult> SendBroadcastAsync(ClaimsPrincipal user,SendBroadcastRequest request,CancellationToken ct)
    {
        _ = Owner(user,true);
        var idempotencyKey=(request.IdempotencyKey??"").Trim();
        if(idempotencyKey.Length<8||idempotencyKey.Length>80||idempotencyKey.Any(char.IsWhiteSpace))
            throw new BusinessException("廣播識別碼格式無效。","BROADCAST_IDEMPOTENCY_INVALID");
        var sourceKey="broadcast:manual:"+idempotencyKey;
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var existing=await connection.QuerySingleOrDefaultAsync<OutboxRow>(new CommandDefinition(
            "SELECT ID AS Id,SOURCE_KEY AS SourceKey,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt FROM NOTIFICATION_OUTBOX WHERE SOURCE_KEY=@SourceKey FOR UPDATE;",
            new{SourceKey=sourceKey},tx,cancellationToken:ct));
        if(existing is not null)
        {
            var previous=JsonSerializer.Deserialize<PlannedDelivery[]>(existing.PayloadJson,MenuPolicies.Json)??[];
            await tx.CommitAsync(ct);
            return new("manual:"+idempotencyKey,sourceKey,true,previous.Length);
        }
        var title=NormalizePlainText(request.Title,120,"廣播標題");
        var message=NormalizePlainText(request.Message,2000,"廣播內容");
        var audienceMode=NormalizeAudienceMode(request.AudienceMode);
        var audienceStaffIds=NormalizeIds(request.AudienceStaffIds);
        var audienceRoles=NormalizeRoles(request.AudienceRoles);
        var priority=NormalizePriority(request.Priority);
        var action=(request.Action??"").Trim().ToLowerInvariant();
        if(action!="open_notifications") throw new BusinessException("手動廣播只能使用受控通知中心動作。","BROADCAST_ACTION_INVALID");
        if(!BroadcastPriorities.Contains(priority,StringComparer.Ordinal)) throw new BusinessException("廣播優先級無效。","BROADCAST_PRIORITY_INVALID");
        ValidateBroadcastAudience(audienceMode,audienceStaffIds,audienceRoles);
        if(request.ExpiresAfterMinutes is <1 or >1440) throw new BusinessException("廣播有效期必須是 1–1,440 分鐘。","BROADCAST_EXPIRY_INVALID");
        var emergency=request.Emergency;
        var requiresAck=request.RequiresAck||emergency;
        if(emergency && (audienceMode!="all"||priority!="high"))
            throw new BusinessException("緊急廣播必須發送給所有有效後台帳號並使用高優先級。","BROADCAST_EMERGENCY_SCOPE_INVALID");
        var reason=emergency?NormalizePlainText(request.Reason,500,"緊急廣播原因"):null;
        var soundId=(request.SoundId??"").Trim();
        if(soundId.Length==0) throw new BusinessException("店內廣播必須選擇系統音效。","BROADCAST_SOUND_REQUIRED");
        if(audienceMode=="staff")
        {
            var existingStaff=await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM `STAFF_MEMBERS` WHERE `IS_ACTIVE`=TRUE AND `ID` IN @Ids;",
                new{Ids=audienceStaffIds},tx,cancellationToken:ct));
            if(existingStaff!=audienceStaffIds.Length) throw new BusinessException("指定店員不存在或目前未啟用。","BROADCAST_TARGET_STAFF_INVALID");
        }
        await NotificationSounds.ValidateAccessAsync(connection,tx,soundId,user,true,ct);
        _ = await sounds.ContentAsync(user,soundId,ct);
        var users=(await LoadActiveUsersAsync(connection,tx,clock.LocalDateTime,ct))
            .Where(x=>MatchesBroadcastAudience(audienceMode,audienceStaffIds,audienceRoles,x)).ToArray();
        var sourceType=emergency?"broadcast_emergency":"broadcast_manual";
        var broadcastId=(emergency?"emergency:":"manual:")+idempotencyKey;
        var popupMode=requiresAck?"critical_modal":"banner";
        var displayMessage=emergency?$"{message}\n\n原因：{reason}":message;
        var match=new MenuNotificationRuleMatch("broadcast",sourceKey,sourceType,1,MenuPolicies.Hash(new{sourceKey}),popupMode,soundId,true)
        {
            Priority=priority,ExpiresAfterMinutes=request.ExpiresAfterMinutes,RequiresAck=requiresAck,
        };
        var content=new MenuNotificationMessage(null,title,displayMessage,popupMode,soundId,true,[$"broadcast:{sourceType}:{idempotencyKey}"])
        {
            PayloadVersion=2,SourceType=sourceType,SourceId=broadcastId,Action=action,
            Priority=priority,ExpiresAfterMinutes=request.ExpiresAfterMinutes,RequiresAck=requiresAck,RuleMatches=[match],
        };
        var deliveries=users.Select(x=>new PlannedDelivery(x.Id,content)).ToArray();
        var inserted=await connection.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO `NOTIFICATION_OUTBOX` (`ID`,`SOURCE_KEY`,`PAYLOAD_JSON`,`CREATED_AT`)
            VALUES (@Id,@SourceKey,@Payload,@Now);
            """,new{Id=Guid.NewGuid().ToString(),SourceKey=sourceKey,
            Payload=JsonSerializer.Serialize(deliveries,MenuPolicies.Json),Now=clock.LocalDateTime},tx,cancellationToken:ct));
        var stored=await connection.QuerySingleAsync<OutboxRow>(new CommandDefinition(
            "SELECT ID AS Id,SOURCE_KEY AS SourceKey,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt FROM NOTIFICATION_OUTBOX WHERE SOURCE_KEY=@SourceKey;",
            new{SourceKey=sourceKey},tx,cancellationToken:ct));
        if(inserted>0)
        {
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@Id,@Actor,'broadcast.manual.sent',@Entity,NULL,@After,@Now);",new
            {
                Id=Guid.NewGuid().ToString(),Actor=Account(user),Entity=broadcastId,
                After=JsonSerializer.Serialize(new{sourceKey,sourceType,emergency,requiresAck,reason,audienceMode,audienceStaffIds,audienceRoles,priority,expiresAfterMinutes=request.ExpiresAfterMinutes,recipientCount=deliveries.Length},MenuPolicies.Json),Now=clock.LocalDateTime
            },tx,cancellationToken:ct));
        }
        await tx.CommitAsync(ct);
        var storedDeliveries=JsonSerializer.Deserialize<PlannedDelivery[]>(stored.PayloadJson,MenuPolicies.Json)??[];
        return new(broadcastId,sourceKey,inserted==0,storedDeliveries.Length);
    }

    private static string NormalizePlainText(string? value,int maxLength,string field)
    {
        var normalized=(value??"").Trim();
        if(normalized.Length==0||normalized.Length>maxLength||normalized.Contains('\0'))
            throw new BusinessException($"{field}不可為空且不可超過 {maxLength} 字。","BROADCAST_TEXT_INVALID");
        return normalized;
    }
    private static string NormalizeAudienceMode(string? value)
        => string.IsNullOrWhiteSpace(value)?"all":value.Trim().ToLowerInvariant();
    private static string[] NormalizeIds(IEnumerable<string>? values)
        => (values??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    private static string[] NormalizeRoles(IEnumerable<string>? values)
        => (values??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    private static string NormalizePriority(string? value)
        => value?.Trim().ToLowerInvariant()??"";
    private static void ValidateBroadcastAudience(string mode,string[] staffIds,string[] roles)
    {
        if(!BroadcastAudienceModes.Contains(mode,StringComparer.Ordinal)||
           (mode=="roles"&&roles.Length==0)||(mode=="staff"&&staffIds.Length==0)||
           (mode is not "roles" and not "staff" && (roles.Length>0||staffIds.Length>0))||
           roles.Any(role=>!BroadcastRoles.Contains(role,StringComparer.Ordinal)))
            throw new BusinessException("廣播受眾設定無效。","BROADCAST_AUDIENCE_INVALID");
    }
    private static bool MatchesBroadcastAudience(string mode,string[] staffIds,string[] roles,UserRow user)
        => mode switch
        {
            "all" => true,
            "working_today" => user.StaffId is not null&&user.IsWorkingToday,
            "staff" => user.StaffId is not null&&staffIds.Contains(user.StaffId,StringComparer.Ordinal),
            "roles" => user.StaffId is not null&&user.IsWorkingToday&&roles.Any(role=>user.ActiveRoles.Contains(role,StringComparer.Ordinal)),
            _ => false,
        };
    private static async Task<IReadOnlyList<UserRow>> LoadActiveUsersAsync(MySqlConnection connection,MySqlTransaction? tx,DateTime now,CancellationToken ct)
        => (await connection.QueryAsync<UserRow>(new CommandDefinition("""
            SELECT U.`ID` AS Id,U.`STAFF_MEMBER_ID` AS StaffId,
                   COALESCE(D.`IS_WORKING`,P.`IS_WORKING`,S.`IS_WORKING`,TRUE) AS IsWorkingToday,
                   COALESCE(D.`ACTIVE_ROLES_JSON`,
                       CASE WHEN COALESCE(D.`IS_WORKING`,P.`IS_WORKING`,S.`IS_WORKING`,TRUE)=TRUE
                            THEN COALESCE(P.`SCHEDULED_ROLES_JSON`,'["service"]') ELSE '[]' END) AS ActiveRolesJson
            FROM `ADMIN_USERS` U
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID`=U.`STAFF_MEMBER_ID` AND S.`WORK_DATE`=DATE(@Now)
            LEFT JOIN `STAFF_DAILY_WORK_MODES` D ON D.`STAFF_MEMBER_ID`=U.`STAFF_MEMBER_ID` AND D.`BUSINESS_DATE`=DATE(@Now)
            LEFT JOIN `STAFF_DUTY_PLANS` P ON P.`STAFF_MEMBER_ID`=U.`STAFF_MEMBER_ID` AND P.`BUSINESS_DATE`=DATE(@Now) AND P.`APPROVAL_STATUS`='approved'
            WHERE U.`IS_ACTIVE`=TRUE;
            """,new{Now=now},tx,cancellationToken:ct))).ToArray();

    public static async Task EnsureNominationSchedulesAsync(MySqlConnection connection,MySqlTransaction tx,
        IEnumerable<OrderNomineeRow> nominees,DateTime now,CancellationToken ct,bool nextScheduleRevision=false)
    {
        var candidates=nominees.Where(x=>x.ConfirmationStatus=="confirmed"&&x.RequestedServiceEndsAt>=now).ToArray();
        if(candidates.Length==0) return;
        var settings=(await connection.QueryAsync<SettingsRow>(new CommandDefinition("""
            SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson
            FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY LIKE 'staff:%' ORDER BY OWNER_KEY;
            """,transaction:tx,cancellationToken:ct))).ToArray();
        foreach(var nominee in candidates)
        {
            var maxRevision=await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT MAX(`SCHEDULE_REVISION`) FROM `NOTIFICATION_SCHEDULES` WHERE `ORDER_NOMINEE_ID`=@NomineeId;",
                new{NomineeId=nominee.Id},tx,cancellationToken:ct));
            var scheduleRevision=nextScheduleRevision
                ? Math.Max(1,(maxRevision??0)+1)
                : Math.Max(1,maxRevision??1);
            foreach(var setting in settings)
            {
                var ownerStaffId=setting.OwnerKey.StartsWith("staff:",StringComparison.Ordinal)
                    ? setting.OwnerKey["staff:".Length..] : "";
                if(string.IsNullOrWhiteSpace(ownerStaffId)) continue;
                foreach(var rule in ReadRules(setting.RulesJson).Where(x=>x.IsEnabled&&IsNominationScheduleRule(x)))
                {
                    if(!MatchesNominationScheduleTarget(rule,ownerStaffId,nominee.StaffId)) continue;
                    var dueAt=NominationScheduleDueAt(rule,nominee);
                    if(dueAt<now) continue;
                    var dueAtUtc=ToTaipeiUtc(dueAt);
                    var scheduleKey=$"nomination_schedule:{nominee.Id}:{rule.Id}:{rule.RuleRevision}:{rule.OffsetMinutes!.Value}:{scheduleRevision}:{dueAtUtc:yyyyMMdd'T'HHmmss.ffffff'Z'}";
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT IGNORE INTO `NOTIFICATION_SCHEDULES`
                            (`ID`,`SCHEDULE_KEY`,`SOURCE_TYPE`,`SOURCE_ID`,`ORDER_ID`,`ORDER_NOMINEE_ID`,
                             `OWNER_KEY`,`RULE_ID`,`RULE_TYPE`,`RULE_REVISION`,`RULE_FINGERPRINT`,`OFFSET_MINUTES`,
                             `SCHEDULE_REVISION`,`DUE_AT`,`EXPIRES_AT`,`STATUS`,`CREATED_AT`,`UPDATED_AT`)
                        VALUES (@Id,@ScheduleKey,'nomination_schedule',@SourceId,@OrderId,@NomineeId,
                                @OwnerKey,@RuleId,@RuleType,@RuleRevision,@Fingerprint,@OffsetMinutes,@ScheduleRevision,
                                @DueAt,@ExpiresAt,'pending',@Now,@Now);
                        """,new
                    {
                        Id=Guid.NewGuid().ToString(),ScheduleKey= scheduleKey,SourceId=nominee.Id,
                        OrderId=nominee.OrderId,NomineeId=nominee.Id,OwnerKey=setting.OwnerKey,
                        RuleId=rule.Id,RuleType=rule.RuleType,RuleRevision=rule.RuleRevision,Fingerprint=rule.Fingerprint,
                        OffsetMinutes=rule.OffsetMinutes.Value,ScheduleRevision=scheduleRevision,
                        DueAt=dueAt,ExpiresAt=dueAt.AddMinutes(15),Now=now
                    },tx,cancellationToken:ct));
                }
            }
        }
    }

    public static async Task<IReadOnlyDictionary<string,long>> InvalidateNominationSchedulesAsync(
        MySqlConnection connection,MySqlTransaction tx,string orderId,DateTime now,CancellationToken ct,
        string? nomineeId=null,string reason="nomination_state_changed",
        IReadOnlyCollection<string>? preserveRuleTypes=null)
    {
        var ids=(await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT `ID` FROM `ORDER_NOMINEES`
            WHERE `ORDER_ID`=@OrderId AND (@NomineeId IS NULL OR `ID`=@NomineeId);
            """,new{OrderId=orderId,NomineeId=nomineeId},tx,cancellationToken:ct))).ToArray();
        if(ids.Length==0) return new Dictionary<string,long>(StringComparer.Ordinal);
        var invalidationSql=preserveRuleTypes is {Count: >0} ? """
            UPDATE `NOTIFICATION_SCHEDULES`
            SET `STATUS`='invalidated', `INVALIDATED_AT`=@Now, `LAST_ERROR`=@Reason, `UPDATED_AT`=@Now
            WHERE `ORDER_ID`=@OrderId AND `STATUS`='pending'
              AND (@NomineeId IS NULL OR `ORDER_NOMINEE_ID`=@NomineeId)
              AND `RULE_TYPE` NOT IN @PreserveRuleTypes;
            """ : """
            UPDATE `NOTIFICATION_SCHEDULES`
            SET `STATUS`='invalidated', `INVALIDATED_AT`=@Now, `LAST_ERROR`=@Reason, `UPDATED_AT`=@Now
            WHERE `ORDER_ID`=@OrderId AND `STATUS`='pending'
              AND (@NomineeId IS NULL OR `ORDER_NOMINEE_ID`=@NomineeId);
            """;
        await connection.ExecuteAsync(new CommandDefinition(invalidationSql,new
        {
            OrderId=orderId,NomineeId=nomineeId,Now=now,Reason=reason,
            PreserveRuleTypes=preserveRuleTypes?.ToArray() ?? []
        },tx,cancellationToken:ct));
        var revisions=(await connection.QueryAsync<ScheduleRevisionRow>(new CommandDefinition("""
            SELECT `ORDER_NOMINEE_ID` AS NomineeId, MAX(`SCHEDULE_REVISION`) AS MaxRevision
            FROM `NOTIFICATION_SCHEDULES`
            WHERE `ORDER_NOMINEE_ID` IN @Ids GROUP BY `ORDER_NOMINEE_ID`;
            """,new{Ids=ids},tx,cancellationToken:ct))).ToDictionary(x=>x.NomineeId,x=>Math.Max(1,x.MaxRevision+1),StringComparer.Ordinal);
        foreach(var id in ids) revisions.TryAdd(id,1);
        return revisions;
    }

    public async Task ReconcileBusinessSchedulesAsync(CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var now=clock.LocalDateTime;
        var contexts=await LoadBusinessScheduleContextsAsync(connection,tx,now,ct);
        var settings=(await connection.QueryAsync<SettingsRow>(new CommandDefinition("""
            SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson
            FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY LIKE 'staff:%' ORDER BY OWNER_KEY;
            """,transaction:tx,cancellationToken:ct))).ToArray();
        var existing=(await connection.QueryAsync<BusinessScheduleExistingRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `SCHEDULE_KEY` AS ScheduleKey, `SOURCE_ID` AS SourceId,
                   `OWNER_KEY` AS OwnerKey, `RULE_ID` AS RuleId, `RULE_TYPE` AS RuleType,
                   `RULE_REVISION` AS RuleRevision, `RULE_FINGERPRINT` AS RuleFingerprint,
                   `OFFSET_MINUTES` AS OffsetMinutes, `SCHEDULE_REVISION` AS ScheduleRevision,
                   `DUE_AT` AS DueAt, `STATUS` AS Status
            FROM `NOTIFICATION_SCHEDULES` WHERE `SOURCE_TYPE`='business_schedule';
            """,transaction:tx,cancellationToken:ct))).ToArray();
        var plans=new List<BusinessSchedulePlan>();
        foreach(var context in contexts)
        {
            var sourceRows=existing.Where(x=>x.SourceId==context.SourceId).ToArray();
            foreach(var setting in settings)
            {
                foreach(var rule in ReadRules(setting.RulesJson).Where(x=>x.IsEnabled&&IsBusinessScheduleRule(x)))
                {
                    var dueAt=BusinessScheduleDueAt(rule,context);
                    if(!dueAt.HasValue || dueAt.Value<now) continue;
                    var offsetMinutes=rule.OffsetMinutes!.Value;
                    var same=sourceRows.FirstOrDefault(x=>x.Status is "pending" or "dispatched" &&
                        x.OwnerKey==setting.OwnerKey && x.RuleId==rule.Id && x.RuleType==rule.RuleType &&
                        x.RuleRevision==rule.RuleRevision && x.RuleFingerprint==rule.Fingerprint &&
                        x.OffsetMinutes==offsetMinutes && x.DueAt==dueAt.Value);
                    var revision=same?.ScheduleRevision ?? Math.Max(1,sourceRows.Select(x=>x.ScheduleRevision).DefaultIfEmpty(0).Max()+1);
                    var dueAtUtc=ToTaipeiUtc(dueAt.Value);
                    plans.Add(new BusinessSchedulePlan(context.SourceId,setting.OwnerKey,rule.Id,rule.RuleType,
                        rule.RuleRevision,rule.Fingerprint,offsetMinutes,revision,dueAt.Value,
                        dueAt.Value.AddMinutes(15),$"business_schedule:{context.SourceId}:{rule.Id}:{rule.RuleRevision}:{offsetMinutes}:{revision}:{dueAtUtc:yyyyMMdd'T'HHmmss.ffffff'Z'}"));
                }
            }
        }
        var desired=plans.Select(x=>x.ScheduleKey).ToHashSet(StringComparer.Ordinal);
        foreach(var stale in existing.Where(x=>x.Status=="pending"&&!desired.Contains(x.ScheduleKey)))
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `NOTIFICATION_SCHEDULES`
                SET `STATUS`='invalidated', `INVALIDATED_AT`=@Now, `LAST_ERROR`='business_schedule_reconciled', `UPDATED_AT`=@Now
                WHERE `ID`=@Id AND `STATUS`='pending';
                """,new{stale.Id,Now=now},tx,cancellationToken:ct));
        foreach(var plan in plans)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO `NOTIFICATION_SCHEDULES`
                    (`ID`,`SCHEDULE_KEY`,`SOURCE_TYPE`,`SOURCE_ID`,`ORDER_ID`,`ORDER_NOMINEE_ID`,
                     `OWNER_KEY`,`RULE_ID`,`RULE_TYPE`,`RULE_REVISION`,`RULE_FINGERPRINT`,`OFFSET_MINUTES`,
                     `SCHEDULE_REVISION`,`DUE_AT`,`EXPIRES_AT`,`STATUS`,`CREATED_AT`,`UPDATED_AT`)
                VALUES (@Id,@ScheduleKey,'business_schedule',@SourceId,NULL,NULL,
                        @OwnerKey,@RuleId,@RuleType,@RuleRevision,@RuleFingerprint,@OffsetMinutes,
                        @ScheduleRevision,@DueAt,@ExpiresAt,'pending',@Now,@Now);
                """,new
            {
                Id=Guid.NewGuid().ToString(),plan.ScheduleKey,plan.SourceId,plan.OwnerKey,plan.RuleId,
                plan.RuleType,plan.RuleRevision,plan.RuleFingerprint,plan.OffsetMinutes,plan.ScheduleRevision,
                plan.DueAt,plan.ExpiresAt,Now=now
            },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> ReconcileBacklogAsync(CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var now=clock.LocalDateTime;
        var setting=await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition("""
            SELECT `OWNER_KEY` AS OwnerKey,`REVISION` AS Revision,`RULES_JSON` AS RulesJson
            FROM `NOTIFICATION_SETTINGS` WHERE `OWNER_KEY`='broadcast';
            """,transaction:tx,cancellationToken:ct));
        if(setting is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }
        var rules=ReadRules(setting.RulesJson).Where(x=>x.IsEnabled&&x.RuleType=="order_backlog").ToArray();
        if(rules.Length==0)
        {
            await tx.CommitAsync(ct);
            return false;
        }
        var pendingCount=await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM `ORDERS`
            WHERE `ORDER_STATUS` IN ('submitted','partially_confirmed')
              AND `STORE_CONFIRMATION_STATUS`='pending';
            """,transaction:tx,cancellationToken:ct));
        var activated=false;
        foreach(var rule in rules)
        {
            var episode=await connection.QuerySingleOrDefaultAsync<BacklogEpisodeRow>(new CommandDefinition("""
                SELECT `EPISODE_ID` AS EpisodeId,`RULE_ID` AS RuleId,`RULE_REVISION` AS RuleRevision,
                       `RULE_FINGERPRINT` AS RuleFingerprint,`THRESHOLD` AS Threshold,
                       `REQUIRED_DURATION_MINUTES` AS RequiredDurationMinutes,`PENDING_COUNT` AS PendingCount,
                       `FIRST_SEEN_AT` AS FirstSeenAt,`LAST_SEEN_AT` AS LastSeenAt,`ACTIVATED_AT` AS ActivatedAt,
                       `OCCURRENCE_NO` AS OccurrenceNo,`STATUS` AS Status
                FROM `NOTIFICATION_BACKLOG_EPISODES`
                WHERE `RULE_ID`=@RuleId AND `RULE_REVISION`=@RuleRevision AND `RULE_FINGERPRINT`=@Fingerprint
                ORDER BY `CREATED_AT` DESC LIMIT 1 FOR UPDATE;
                """,new{RuleId=rule.Id,RuleRevision=rule.RuleRevision,Fingerprint=rule.Fingerprint},tx,cancellationToken:ct));
            if(pendingCount<rule.BacklogThreshold)
            {
                if(episode is not null && episode.Status!="resolved")
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `NOTIFICATION_BACKLOG_EPISODES`
                        SET `PENDING_COUNT`=@PendingCount,`LAST_SEEN_AT`=@Now,`STATUS`='resolved',`UPDATED_AT`=@Now
                        WHERE `EPISODE_ID`=@EpisodeId;
                        """,new{PendingCount=pendingCount,Now=now,episode.EpisodeId},tx,cancellationToken:ct));
                continue;
            }
            if(episode is null||episode.Status is "resolved" or "stopped")
            {
                var episodeId=$"backlog:{MenuPolicies.Hash(new{rule.Id,rule.RuleRevision,rule.Fingerprint,now})}";
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `NOTIFICATION_BACKLOG_EPISODES`
                        (`EPISODE_ID`,`RULE_ID`,`RULE_REVISION`,`RULE_FINGERPRINT`,`THRESHOLD`,
                         `REQUIRED_DURATION_MINUTES`,`PENDING_COUNT`,`FIRST_SEEN_AT`,`LAST_SEEN_AT`,
                         `OCCURRENCE_NO`,`STATUS`,`CREATED_AT`,`UPDATED_AT`)
                    VALUES (@EpisodeId,@RuleId,@RuleRevision,@Fingerprint,@Threshold,
                            @Duration,@PendingCount,@Now,@Now,0,'watching',@Now,@Now);
                    """,new
                {
                    EpisodeId=episodeId,RuleId=rule.Id,RuleRevision=rule.RuleRevision,Fingerprint=rule.Fingerprint,
                    Threshold=rule.BacklogThreshold,Duration=rule.BacklogDurationMinutes,PendingCount=pendingCount,Now=now,
                },tx,cancellationToken:ct));
                episode=new BacklogEpisodeRow
                {
                    EpisodeId=episodeId,RuleId=rule.Id,RuleRevision=rule.RuleRevision,RuleFingerprint=rule.Fingerprint,
                    Threshold=rule.BacklogThreshold,RequiredDurationMinutes=rule.BacklogDurationMinutes,
                    PendingCount=pendingCount,FirstSeenAt=now,LastSeenAt=now,OccurrenceNo=0,Status="watching",
                };
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `NOTIFICATION_BACKLOG_EPISODES`
                    SET `PENDING_COUNT`=@PendingCount,`LAST_SEEN_AT`=@Now,`UPDATED_AT`=@Now
                    WHERE `EPISODE_ID`=@EpisodeId;
                    """,new{PendingCount=pendingCount,Now=now,episode.EpisodeId},tx,cancellationToken:ct));
            }
            if(episode.Status!="watching"||episode.OccurrenceNo!=0||now<episode.FirstSeenAt.AddMinutes(rule.BacklogDurationMinutes)) continue;
            var episodeIdForDelivery=episode.EpisodeId;
            var users=(await LoadActiveUsersAsync(connection,tx,now,ct))
                .Where(x=>MatchesBroadcastAudience(rule.AudienceMode,rule.AudienceStaffIds,rule.AudienceRoles,x)).ToArray();
            var groupKey=$"broadcast:backlog:{episodeIdForDelivery}";
            var match=new MenuNotificationRuleMatch("broadcast",rule.Id,"order_backlog",rule.RuleRevision,rule.Fingerprint,
                rule.RequiresAck?"critical_modal":rule.PopupMode,rule.SoundId,true)
            {
                Priority=rule.Priority,ExpiresAfterMinutes=rule.ExpiresAfterMinutes,RequiresAck=rule.RequiresAck,
                RepeatIntervalMinutes=rule.RepeatIntervalMinutes,MaxOccurrences=rule.MaxOccurrences,
            };
            var content=new MenuNotificationMessage(null,"待處理訂單堆積",$"目前有 {pendingCount} 筆待店內處理訂單",
                match.PopupMode,rule.SoundId,true,[$"broadcast:{rule.Id}:{rule.RuleRevision}"])
            {
                PayloadVersion=2,SourceType="order_backlog",SourceId=episodeIdForDelivery,Action="open_notifications",
                Priority=rule.Priority,ExpiresAfterMinutes=rule.ExpiresAfterMinutes,RequiresAck=rule.RequiresAck,
                GroupKey=groupKey,EpisodeId=episodeIdForDelivery,PresentationId=groupKey+":1",RuleMatches=[match],
            };
            var deliveries=users.Select(x=>new PlannedDelivery(x.Id,content)).ToArray();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO `NOTIFICATION_OUTBOX` (`ID`,`SOURCE_KEY`,`PAYLOAD_JSON`,`CREATED_AT`)
                VALUES (@Id,@SourceKey,@Payload,@Now);
                UPDATE `NOTIFICATION_BACKLOG_EPISODES`
                SET `ACTIVATED_AT`=@Now,`OCCURRENCE_NO`=1,`STATUS`='active',`UPDATED_AT`=@Now
                WHERE `EPISODE_ID`=@EpisodeId AND `STATUS`='watching' AND `OCCURRENCE_NO`=0;
                """,new
            {
                Id=Guid.NewGuid().ToString(),SourceKey=groupKey+":1",
                Payload=JsonSerializer.Serialize(deliveries,MenuPolicies.Json),Now=now,EpisodeId=episodeIdForDelivery,
            },tx,cancellationToken:ct));
            activated=true;
        }
        await tx.CommitAsync(ct);
        return activated;
    }

    private async Task<IReadOnlyList<BusinessScheduleContext>> LoadBusinessScheduleContextsAsync(
        MySqlConnection connection,MySqlTransaction tx,DateTime now,CancellationToken ct)
    {
        var overrideRow=await connection.QuerySingleOrDefaultAsync<BusinessOverrideRow>(new CommandDefinition("""
            SELECT `BUSINESS_DATE` AS BusinessDate, `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt,
                   `EXPIRES_AT` AS ExpiresAt
            FROM `ORDERING_BUSINESS_DAY_OVERRIDE`
            WHERE `ID`='default' AND `ENABLED`=TRUE AND `EXPIRES_AT`>@Now LIMIT 1;
            """,new{Now=now},tx,cancellationToken:ct));
        if(overrideRow is not null)
            return [new BusinessScheduleContext($"override:{overrideRow.BusinessDate:yyyy-MM-dd}",
                overrideRow.BusinessDate,overrideRow.StartsAt,overrideRow.EndsAt,
                null,null,null,"open")];

        var periods=(await connection.QueryAsync<BusinessScheduleContext>(new CommandDefinition("""
            SELECT CONCAT('period:',`ID`) AS SourceId, `BUSINESS_DATE` AS BusinessDate,
                   `STARTS_AT` AS StartsAt, `ENDS_AT` AS ScheduledEndsAt,
                   `ACTUAL_OPENED_AT` AS ActualOpenedAt, `PROJECTED_CLOSE_AT` AS ProjectedCloseAt,
                   `ACTUAL_CLOSED_AT` AS ActualClosedAt, `PERIOD_STATUS` AS PeriodStatus
            FROM `BUSINESS_PERIODS`
            WHERE `SETTLED_AT` IS NULL AND `PERIOD_STATUS` IN ('open','closed');
            """,transaction:tx,cancellationToken:ct))).ToList();
        var dates=periods.Select(x=>DateOnly.FromDateTime(x.BusinessDate)).ToHashSet();
        var settings=await connection.QuerySingleOrDefaultAsync<BusinessScheduleSettingsRow>(new CommandDefinition("""
            SELECT `BUSINESS_DAY_START_MINUTE` AS StartsAtMinute,
                   `BUSINESS_DAY_END_MINUTE` AS EndsAtMinute,
                   `BUSINESS_DAY_ENDS_NEXT_DAY` AS EndsNextDay,
                   `UPDATED_BY` AS UpdatedBy
            FROM `ORDERING_SETTINGS` WHERE `ID`='default' LIMIT 1;
            """,transaction:tx,cancellationToken:ct));
        if(settings?.UpdatedBy is not null && TryBusinessWindow(settings,DateOnly.FromDateTime(now),out _))
        {
            foreach(var date in new[] { DateOnly.FromDateTime(now).AddDays(-1),DateOnly.FromDateTime(now),DateOnly.FromDateTime(now).AddDays(1) })
            {
                if(dates.Contains(date) || !TryBusinessWindow(settings,date,out var window)) continue;
                periods.Add(new BusinessScheduleContext($"date:{date:yyyy-MM-dd}",date.ToDateTime(TimeOnly.MinValue),window.StartsAt,
                    window.EndsAt,null,null,null,"scheduled"));
            }
        }
        return periods;
    }

    private static bool IsBusinessScheduleRule(MenuNotificationRule rule)
        => rule.RuleType is "business_opening_soon" or "business_closing_soon" &&
           rule.OffsetMinutes.HasValue;
    private async Task<bool> IsBusinessScheduleDeliveryActiveAsync(MySqlConnection connection,MySqlTransaction tx,
        string sourceKey,MenuNotificationMessage message,IReadOnlyList<MenuNotificationRuleMatch> matches,
        DateTime now,CancellationToken ct)
    {
        if(!sourceKey.StartsWith("schedule:",StringComparison.Ordinal)) return false;
        var schedule=await connection.QuerySingleOrDefaultAsync<ScheduleDeliveryRow>(new CommandDefinition("""
            SELECT `SOURCE_ID` AS SourceId, `OWNER_KEY` AS OwnerKey, `RULE_ID` AS RuleId,
                   `RULE_TYPE` AS RuleType, `RULE_REVISION` AS RuleRevision,
                   `RULE_FINGERPRINT` AS RuleFingerprint, `DUE_AT` AS DueAt,
                   `EXPIRES_AT` AS ExpiresAt, `STATUS` AS Status
            FROM `NOTIFICATION_SCHEDULES` WHERE `ID`=@Id LIMIT 1;
            """,new{Id=sourceKey["schedule:".Length..]},tx,cancellationToken:ct));
        var matched=matches.FirstOrDefault(x=>x.RuleType is "business_opening_soon" or "business_closing_soon");
        if(schedule is null || matched is null || schedule.Status=="invalidated" || schedule.ExpiresAt<=now ||
            schedule.RuleId!=matched.RuleId || schedule.RuleType!=matched.RuleType ||
            schedule.RuleRevision!=matched.RuleRevision || schedule.RuleFingerprint!=matched.Fingerprint)
            return false;
        var setting=await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition(
            "SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY=@OwnerKey;",
            new{schedule.OwnerKey},tx,cancellationToken:ct));
        var rule=setting is null ? null : ReadRules(setting.RulesJson).FirstOrDefault(x=>x.Id==schedule.RuleId);
        var context=(await LoadBusinessScheduleContextsAsync(connection,tx,now,ct))
            .FirstOrDefault(x=>x.SourceId==schedule.SourceId);
        var currentDue=rule is null || context is null ? null : BusinessScheduleDueAt(rule,context);
        return rule is not null && IsBusinessScheduleRule(rule) && currentDue==schedule.DueAt &&
               rule.RuleRevision==schedule.RuleRevision && rule.Fingerprint==schedule.RuleFingerprint &&
               matched.OwnerKey==schedule.OwnerKey && message.SourceType=="business_schedule";
    }
    private static DateTime? BusinessScheduleDueAt(MenuNotificationRule rule,BusinessScheduleContext context)
    {
        if(rule.RuleType=="business_opening_soon")
            return context.ActualOpenedAt is null && context.PeriodStatus is not ("closed" or "settled")
                ? context.StartsAt.AddMinutes(-rule.OffsetMinutes!.Value) : null;
        if(context.ActualClosedAt is not null || context.PeriodStatus is "closed" or "settled") return null;
        var closeAt=context.ActualOpenedAt is null
            ? context.ScheduledEndsAt
            : context.ProjectedCloseAt ?? context.ScheduledEndsAt;
        return closeAt.AddMinutes(-rule.OffsetMinutes!.Value);
    }
    private static bool TryBusinessWindow(BusinessScheduleSettingsRow settings,DateOnly date,
        out BusinessWindow window)
    {
        window=default!;
        if(settings.StartsAtMinute is <0 or >1439 || settings.EndsAtMinute is <0 or >1439 ||
           (!settings.EndsNextDay && settings.EndsAtMinute<=settings.StartsAtMinute) ||
           (settings.EndsNextDay && settings.EndsAtMinute>settings.StartsAtMinute)) return false;
        var startsAt=date.ToDateTime(TimeOnly.MinValue).AddMinutes(settings.StartsAtMinute);
        var endDate=settings.EndsNextDay?date.AddDays(1):date;
        window=new BusinessWindow(date,startsAt,endDate.ToDateTime(TimeOnly.MinValue).AddMinutes(settings.EndsAtMinute));
        return window.EndsAt>window.StartsAt;
    }

    public async Task<bool> DispatchScheduleOneAsync(CancellationToken ct)
    {
        try
        {
            await using var connection=await db.CreateOpenConnectionAsync(ct);
            await using var tx=await connection.BeginTransactionAsync(ct);
            var now=clock.LocalDateTime;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `NOTIFICATION_SCHEDULES`
                SET `STATUS`='expired', `UPDATED_AT`=@Now, `LAST_ERROR`='schedule_window_expired'
                WHERE `STATUS`='pending' AND `EXPIRES_AT`<=@Now;
                """,new{Now=now},tx,cancellationToken:ct));
            var schedule=await connection.QuerySingleOrDefaultAsync<ScheduleRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `SCHEDULE_KEY` AS ScheduleKey, `SOURCE_TYPE` AS SourceType, `SOURCE_ID` AS SourceId,
                       `ORDER_ID` AS OrderId, `ORDER_NOMINEE_ID` AS NomineeId, `OWNER_KEY` AS OwnerKey,
                       `RULE_ID` AS RuleId, `RULE_TYPE` AS RuleType, `RULE_REVISION` AS RuleRevision,
                       `RULE_FINGERPRINT` AS RuleFingerprint, `DUE_AT` AS DueAt
                FROM `NOTIFICATION_SCHEDULES`
                WHERE `STATUS`='pending' AND `DUE_AT`<=@Now AND `EXPIRES_AT`>@Now
                ORDER BY `DUE_AT`,`ID` LIMIT 1 FOR UPDATE SKIP LOCKED;
                """,new{Now=now},tx,cancellationToken:ct));
            if(schedule is null)
            {
                await tx.CommitAsync(ct);
                return false;
            }
            var setting=await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition(
                "SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY=@OwnerKey;",
                new{schedule.OwnerKey},tx,cancellationToken:ct));
            var rule=setting is null ? null : ReadRules(setting.RulesJson).FirstOrDefault(x=>x.Id==schedule.RuleId);
            if(rule is null || !rule.IsEnabled || rule.RuleRevision!=schedule.RuleRevision ||
                !string.Equals(rule.Fingerprint,schedule.RuleFingerprint,StringComparison.Ordinal) ||
                rule.RuleType!=schedule.RuleType)
            {
                await InvalidateScheduleRowAsync(connection,tx,schedule.Id,now,"nomination_rule_changed",ct);
                await tx.CommitAsync(ct);
                return true;
            }
            ScheduleSourceRow? source=null;
            BusinessScheduleContext? business=null;
            if(schedule.SourceType=="nomination_schedule")
            {
                source=await connection.QuerySingleOrDefaultAsync<ScheduleSourceRow>(new CommandDefinition("""
                    SELECT O.`ID` AS OrderId, O.`ORDER_NUMBER` AS OrderNumber,
                           O.`ORDER_STATUS` AS OrderStatus, N.`STAFF_ID` AS StaffId,
                           N.`STAFF_NAME_SNAPSHOT` AS StaffName, N.`CONFIRMATION_STATUS` AS ConfirmationStatus
                    FROM `ORDER_NOMINEES` N JOIN `ORDERS` O ON O.`ID`=N.`ORDER_ID`
                    WHERE N.`ID`=@NomineeId;
                    """,new{NomineeId=schedule.NomineeId},tx,cancellationToken:ct));
                var completedEndedSchedule=source?.OrderStatus=="completed"&&schedule.RuleType=="nomination_ended";
                if(!IsNominationScheduleRule(rule) || source is null || source.ConfirmationStatus!="confirmed" ||
                    !MatchesNominationScheduleTarget(rule,schedule.OwnerKey["staff:".Length..],source.StaffId) ||
                    (source.OrderStatus is "cancelled" or "canceled" or "expired" or "rejected" ||
                     (source.OrderStatus=="completed"&&!completedEndedSchedule)))
                {
                    await InvalidateScheduleRowAsync(connection,tx,schedule.Id,now,"nomination_source_inactive",ct);
                    await tx.CommitAsync(ct);
                    return true;
                }
            }
            else if(schedule.SourceType=="business_schedule")
            {
                if(!IsBusinessScheduleRule(rule))
                {
                    await InvalidateScheduleRowAsync(connection,tx,schedule.Id,now,"business_rule_invalid",ct);
                    await tx.CommitAsync(ct);
                    return true;
                }
                business=(await LoadBusinessScheduleContextsAsync(connection,tx,now,ct))
                    .FirstOrDefault(x=>x.SourceId==schedule.SourceId);
                var currentDue=business is null ? null : BusinessScheduleDueAt(rule,business);
                if(business is null || !currentDue.HasValue || currentDue.Value!=schedule.DueAt || currentDue.Value<now)
                {
                    await InvalidateScheduleRowAsync(connection,tx,schedule.Id,now,"business_schedule_changed",ct);
                    await tx.CommitAsync(ct);
                    return true;
                }
            }
            else
            {
                await InvalidateScheduleRowAsync(connection,tx,schedule.Id,now,"unknown_schedule_source",ct);
                await tx.CommitAsync(ct);
                return true;
            }
            var accountRows=(await connection.QueryAsync<UserRow>(new CommandDefinition(
                "SELECT ID AS Id,STAFF_MEMBER_ID AS StaffId FROM ADMIN_USERS WHERE IS_ACTIVE=TRUE AND STAFF_MEMBER_ID=@StaffId;",
                new{StaffId=schedule.OwnerKey["staff:".Length..]},tx,cancellationToken:ct))).ToArray();
            var title=rule.RuleType switch
            {
                "nomination_starting"=>"指名時段即將開始",
                "nomination_ending"=>"指名服務即將結束",
                "nomination_ended"=>"指名服務已結束",
                "business_opening_soon"=>"預定開店提醒",
                "business_closing_soon"=>"預定關店提醒",
                _=>"指名時段提醒",
            };
            var message=schedule.SourceType=="business_schedule"
                ? $"預定時間 {((rule.RuleType=="business_opening_soon"?business!.StartsAt:
                    business!.ActualOpenedAt is null?business.ScheduledEndsAt:business.ProjectedCloseAt??business.ScheduledEndsAt)):MM/dd HH:mm}"
                : $"訂單 {source!.OrderNumber} · {source.StaffName}";
            var content=new MenuNotificationMessage(schedule.SourceType=="business_schedule"?null:schedule.OrderId,title,
                message,rule.PopupMode,rule.SoundId,false,[])
            {
                PayloadVersion=2,SourceType=schedule.SourceType,SourceId=schedule.SourceId,
                Action=schedule.SourceType=="business_schedule"?"open_notifications":"view_nomination",
                RuleMatches=[new MenuNotificationRuleMatch(schedule.OwnerKey,rule.Id,rule.RuleType,
                    rule.RuleRevision,rule.Fingerprint,rule.PopupMode,rule.SoundId,false)],
            };
            var deliveries=accountRows.Select(x=>new PlannedDelivery(x.Id,content)).ToArray();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO `NOTIFICATION_OUTBOX` (`ID`,`SOURCE_KEY`,`PAYLOAD_JSON`,`CREATED_AT`)
                VALUES (@Id,@SourceKey,@Payload,@Now);
                UPDATE `NOTIFICATION_SCHEDULES`
                SET `STATUS`='dispatched', `PROCESSED_AT`=@Now, `UPDATED_AT`=@Now
                WHERE `ID`=@ScheduleId AND `STATUS`='pending';
                """,new{Id=Guid.NewGuid().ToString(),SourceKey="schedule:"+schedule.Id,
                Payload=JsonSerializer.Serialize(deliveries,MenuPolicies.Json),Now=now,ScheduleId=schedule.Id},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return true;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            throw;
        }
    }

    private static Task InvalidateScheduleRowAsync(MySqlConnection connection,MySqlTransaction tx,
        string id,DateTime now,string reason,CancellationToken ct)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `NOTIFICATION_SCHEDULES`
            SET `STATUS`='invalidated', `INVALIDATED_AT`=@Now, `LAST_ERROR`=@Reason, `UPDATED_AT`=@Now
            WHERE `ID`=@Id AND `STATUS`='pending';
            """,new{Id=id,Now=now,Reason=reason},tx,cancellationToken:ct));

    private static bool IsNominationScheduleRule(MenuNotificationRule rule)
        => rule.RuleType is "nomination_starting" or "nomination_ending" or "nomination_ended" &&
           rule.OffsetMinutes.HasValue && rule.TargetMode is "self" or "staff" or "all";
    private static bool MatchesNominationScheduleTarget(MenuNotificationRule rule,string ownerStaffId,string nomineeStaffId)
        => rule.TargetMode switch
        {
            "all" => true,
            "self" => string.Equals(ownerStaffId,nomineeStaffId,StringComparison.Ordinal),
            "staff" => string.Equals(rule.TargetStaffId,nomineeStaffId,StringComparison.Ordinal),
            _ => false,
        };
    private static DateTime NominationScheduleDueAt(MenuNotificationRule rule,OrderNomineeRow nominee)
        => rule.RuleType switch
        {
            "nomination_starting" => nominee.RequestedStartsAt.AddMinutes(-rule.OffsetMinutes!.Value),
            "nomination_ending" => nominee.RequestedServiceEndsAt.AddMinutes(-rule.OffsetMinutes!.Value),
            "nomination_ended" => nominee.RequestedServiceEndsAt.AddMinutes(rule.OffsetMinutes!.Value),
            _ => nominee.RequestedStartsAt,
        };
    private static DateTime ToTaipeiUtc(DateTime value)
        => new DateTimeOffset(DateTime.SpecifyKind(value,DateTimeKind.Unspecified),TimeSpan.FromHours(8)).UtcDateTime;

    public static async Task EnqueueAsync(MySqlConnection connection,MySqlTransaction tx,NewOrderAggregate order,CancellationToken ct)
    {
        var hasMenu=order.Items.Any(x=>x.ItemType is "menu_item" or "menu_set");
        var hasNomination=order.Nominees.Count>0;
        if(!hasMenu&&!hasNomination) return;
        var snapshot=string.IsNullOrWhiteSpace(order.MenuSnapshotJson)?null:JsonSerializer.Deserialize<MenuOrderSnapshot>(order.MenuSnapshotJson,MenuPolicies.Json);
        var champagne=hasMenu&&snapshot?.Lines.Any(x=>x.EventCategories.Contains("champagne_tower"))==true;
        var sourceType=hasMenu?"order_submitted":"nomination_submitted";
        var sourceKey=$"{(hasMenu?"order":"nomination")}:{order.Id}:submitted";
        var settings=(await connection.QueryAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS ORDER BY OWNER_KEY;",transaction:tx,cancellationToken:ct))).ToDictionary(x=>x.OwnerKey);
        var users=await LoadActiveUsersAsync(connection,tx,order.SubmittedAt,ct);
        var deliveries=new List<PlannedDelivery>();
        foreach(var user in users)
        {
            var matches=new List<(MenuNotificationRule Rule,bool Broadcast,string OwnerKey,long Revision)>();
            foreach(var key in new[]{"broadcast","staff:"+user.StaffId})
                if(settings.TryGetValue(key,out var entry))
                {
                    var broadcast=key=="broadcast";
                    matches.AddRange(ReadRules(entry.RulesJson)
                        .Where(x=>MatchesSubmission(x,broadcast,hasMenu,champagne,order.Nominees,user.StaffId,user))
                        .Select(x=>(x,broadcast,key,entry.Revision)));
                }
            if(matches.Count==0) continue;
            var isBroadcast=matches.Any(x=>x.Broadcast);
            var chosen=isBroadcast?matches.Where(x=>x.Broadcast).OrderByDescending(x=>x.Rule.Priority=="high").ThenByDescending(x=>x.Rule.RuleType=="champagne_order_received").ThenBy(x=>x.Rule.Id,StringComparer.Ordinal).First().Rule:matches.FirstOrDefault(x=>x.Rule.SoundId is not null).Rule;
            var broadcastMatches=matches.Where(x=>x.Broadcast).Select(x=>x.Rule).ToArray();
            var requiresAck=broadcastMatches.Any(x=>x.RequiresAck);
            var mode=requiresAck?"critical_modal":isBroadcast?"banner":matches.Any(x=>x.Rule.PopupMode=="sticky")?"sticky":matches.Any(x=>x.Rule.PopupMode=="toast")?"toast":"none";
            var text=order.StoreConfirmationStatus=="pending"?"待店內確認":order.Status=="confirmed"?"已成立":"已提交，等待指名確認";
            var designated=matches.Any(x=>x.Rule.RuleType=="designated_order_received");
            var champagneMatch=matches.Any(x=>x.Rule.RuleType=="champagne_order_received");
            var title=champagneMatch?"收到香檳塔點餐訂單":hasMenu&&designated?"收到點餐／指名訂單":hasMenu?"收到點餐訂單":"收到指名訂單";
            var nomineeText=hasNomination?" · 指名："+string.Join("、",order.Nominees.Select(x=>x.StaffName).Distinct(StringComparer.Ordinal)):"";
            deliveries.Add(new(user.Id,new(order.Id,title,$"訂單 {order.OrderNumber}{nomineeText} · {text}",mode,chosen?.SoundId,isBroadcast,
                matches.Select(x=>$"{(x.Broadcast?"broadcast":"personal")}:{x.Rule.Id}:{x.Revision}").ToArray())
            {
                PayloadVersion = 2,
                SourceType = sourceType,
                SourceId = order.Id,
                Action = hasMenu?"view_order":"view_nomination",
                Priority = broadcastMatches.Any(x=>x.Priority=="high")?"high":"normal",
                ExpiresAfterMinutes = broadcastMatches.Select(x=>x.ExpiresAfterMinutes).DefaultIfEmpty(15).Min(),
                RequiresAck = requiresAck,
                GroupKey = sourceKey,
                PresentationId = sourceKey+":1",
                RuleMatches = matches.Select(x=>new MenuNotificationRuleMatch(x.OwnerKey,x.Rule.Id,x.Rule.RuleType,x.Rule.RuleRevision,x.Rule.Fingerprint,x.Broadcast?(x.Rule.RequiresAck?"critical_modal":"banner"):x.Rule.PopupMode,x.Rule.SoundId,x.Broadcast)
                {
                    Priority=x.Rule.Priority,ExpiresAfterMinutes=x.Rule.ExpiresAfterMinutes,RequiresAck=x.Rule.RequiresAck,
                    RepeatIntervalMinutes=x.Broadcast?x.Rule.RepeatIntervalMinutes:0,
                    MaxOccurrences=x.Broadcast?x.Rule.MaxOccurrences:1,
                }).ToArray(),
            }));
        }
        // Even an empty audience is snapshotted. Later settings must not replay this order.
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_OUTBOX (ID,SOURCE_KEY,PAYLOAD_JSON,CREATED_AT) VALUES (@Id,@Source,@Payload,@Now);",
            new{Id=Guid.NewGuid().ToString(),Source=sourceKey,Payload=JsonSerializer.Serialize(deliveries,MenuPolicies.Json),Now=order.SubmittedAt},tx,cancellationToken:ct));
    }

    private static bool MatchesSubmission(MenuNotificationRule rule,bool broadcast,bool hasMenu,bool champagne,IReadOnlyList<NewOrderNominee> nominees,string? observerStaffId,UserRow user)
    {
        if(!rule.IsEnabled) return false;
        if(broadcast) return hasMenu&&(rule.RuleType=="order_received"||(champagne&&rule.RuleType=="champagne_order_received")) &&
            MatchesBroadcastAudience(rule.AudienceMode,rule.AudienceStaffIds,rule.AudienceRoles,user);
        if(rule.RuleType=="order_received") return hasMenu;
        if(rule.RuleType=="champagne_order_received") return hasMenu&&champagne;
        if(rule.RuleType!="designated_order_received"||nominees.Count==0) return false;
        return rule.TargetMode switch
        {
            "all" => true,
            "self" => observerStaffId is not null&&nominees.Any(x=>x.StaffId==observerStaffId),
            "staff" => rule.TargetStaffId is not null&&nominees.Any(x=>x.StaffId==rule.TargetStaffId),
            _ => false,
        };
    }

    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        string? outboxId=null;
        try
        {
            await using var connection=await db.CreateOpenConnectionAsync(ct);
            await using var tx=await connection.BeginTransactionAsync(ct);
            var now=clock.LocalDateTime;
            var row=await connection.QuerySingleOrDefaultAsync<OutboxRow>(new CommandDefinition("SELECT ID AS Id,SOURCE_KEY AS SourceKey,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt,ATTEMPTS AS Attempts FROM NOTIFICATION_OUTBOX WHERE PROCESSED_AT IS NULL AND IS_QUARANTINED=FALSE AND (NEXT_ATTEMPT_AT IS NULL OR NEXT_ATTEMPT_AT<=@Now) ORDER BY CREATED_AT,ID LIMIT 1 FOR UPDATE SKIP LOCKED;",new{Now=now},tx,cancellationToken:ct));
            if(row is null) return false;
            outboxId=row.Id;
            var deliveries=JsonSerializer.Deserialize<PlannedDelivery[]>(row.PayloadJson,MenuPolicies.Json)??[];
            foreach(var delivery in deliveries)
            {
                var user=await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition("SELECT ID AS Id,STAFF_MEMBER_ID AS StaffId FROM ADMIN_USERS WHERE ID=@Id AND IS_ACTIVE=TRUE;",new{Id=delivery.AccountId},tx,cancellationToken:ct));
                if(user is null) continue;
                var latest=(await connection.QueryAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY IN @Owners;",new{Owners=new[]{"broadcast","staff:"+user.StaffId}},tx,cancellationToken:ct))).ToArray();
                var matches=delivery.Content.SourceType is "broadcast_manual" or "broadcast_emergency"
                    ? delivery.Content.RuleMatches
                    : delivery.Content.RuleMatches.Length>0
                        ? ResolvePlannedMatches(delivery.Content.RuleMatches,latest)
                        : ResolveLegacyMatches(delivery.Content,latest);
                if(matches.Length==0) continue;
                var normalized=NormalizeMessage(delivery.Content);
                var groupKey=string.IsNullOrWhiteSpace(normalized.GroupKey)?row.SourceKey:normalized.GroupKey;
                normalized=NormalizeMessage(normalized with
                {
                    GroupKey=groupKey,
                    PresentationId=$"{groupKey}:{normalized.OccurrenceNo}",
                });
                var content=ComposeContent(normalized,matches);
                // Canceled orders and invalidated schedule sources remain history, but are never presented as new work.
                var active=normalized.SourceType switch
                {
                    "order_submitted" => !string.IsNullOrWhiteSpace(normalized.OrderId) &&
                        await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM ORDERS WHERE ID=@Id AND ORDER_STATUS NOT IN ('cancelled','canceled','expired');",new{Id=normalized.OrderId},tx,cancellationToken:ct)),
                    "nomination_schedule" => !string.IsNullOrWhiteSpace(normalized.SourceId) &&
                        await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                            SELECT COUNT(*)>0 FROM ORDER_NOMINEES N JOIN ORDERS O ON O.ID=N.ORDER_ID
                            WHERE N.ID=@NomineeId AND N.CONFIRMATION_STATUS='confirmed'
                              AND O.ORDER_STATUS NOT IN ('cancelled','canceled','expired','rejected')
                              AND (O.ORDER_STATUS<>'completed' OR @AllowCompleted=TRUE);
                            """,new
                            {
                                NomineeId=normalized.SourceId,
                                AllowCompleted=normalized.RuleMatches.Any(x=>x.RuleType=="nomination_ended")
                            },tx,cancellationToken:ct)),
                    "business_schedule" => await IsBusinessScheduleDeliveryActiveAsync(connection,tx,row.SourceKey,
                        normalized,matches,now,ct),
                    "order_backlog" => !string.IsNullOrWhiteSpace(normalized.SourceId) &&
                        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                            "SELECT COUNT(*)>0 FROM `NOTIFICATION_BACKLOG_EPISODES` WHERE `EPISODE_ID`=@EpisodeId AND `STATUS`='active';",
                            new{EpisodeId=normalized.SourceId},tx,cancellationToken:ct)),
                    _ => true,
                };
                var deliverySourceKey=content.GroupKey??row.SourceKey;
                var deliveryChanged=await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO `NOTIFICATION_DELIVERIES`
                        (`ID`,`SOURCE_KEY`,`RECIPIENT_ACCOUNT_ID`,`PAYLOAD_JSON`,`CREATED_AT`,`EXPIRES_AT`,`OCCURRENCE_NO`,`EPISODE_ID`)
                    VALUES (@Id,@Source,@Account,@Payload,@Created,@Expires,@OccurrenceNo,@EpisodeId)
                    ON DUPLICATE KEY UPDATE
                        `PAYLOAD_JSON`=IF(`WITHDRAWN_AT` IS NULL AND @OccurrenceNo>=`OCCURRENCE_NO`,VALUES(`PAYLOAD_JSON`),`PAYLOAD_JSON`),
                        `EXPIRES_AT`=IF(`WITHDRAWN_AT` IS NULL AND @OccurrenceNo>=`OCCURRENCE_NO`,VALUES(`EXPIRES_AT`),`EXPIRES_AT`),
                        `OCCURRENCE_NO`=GREATEST(`OCCURRENCE_NO`,VALUES(`OCCURRENCE_NO`)),
                        `EPISODE_ID`=COALESCE(VALUES(`EPISODE_ID`),`EPISODE_ID`);
                    """,
                    new
                    {
                        Id=Guid.NewGuid().ToString(),Source=deliverySourceKey,Account=delivery.AccountId,
                        Payload=JsonSerializer.Serialize(content,MenuPolicies.Json),Created=row.CreatedAt,
                        Expires=active?row.CreatedAt.AddMinutes(Math.Clamp(content.ExpiresAfterMinutes,1,1440)):now,
                        OccurrenceNo=content.OccurrenceNo,EpisodeId=content.EpisodeId,
                    },tx,cancellationToken:ct));
                var deliveryId=await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT ID FROM NOTIFICATION_DELIVERIES WHERE SOURCE_KEY=@Source AND RECIPIENT_ACCOUNT_ID=@Account;",
                    new{Source=deliverySourceKey,Account=delivery.AccountId},tx,cancellationToken:ct));
                foreach(var match in matches)
                {
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT IGNORE INTO `NOTIFICATION_DELIVERY_MATCHES`
                            (`DELIVERY_ID`,`MATCH_KEY`,`SOURCE_KEY`,`SOURCE_TYPE`,`SOURCE_ID`,`OWNER_KEY`,`RULE_ID`,
                             `RULE_TYPE`,`RULE_REVISION`,`RULE_FINGERPRINT`,`POPUP_MODE`,`SOUND_ID`,`PRIORITY`,
                             `EXPIRES_AFTER_MINUTES`,`IS_BROADCAST`,`REQUIRES_ACK`,`REPEAT_INTERVAL_MINUTES`,
                             `MAX_OCCURRENCES`,`CREATED_AT`)
                        VALUES (@DeliveryId,@MatchKey,@SourceKey,@SourceType,@SourceId,@OwnerKey,@RuleId,
                                @RuleType,@RuleRevision,@Fingerprint,@PopupMode,@SoundId,@Priority,
                                @ExpiresAfterMinutes,@IsBroadcast,@RequiresAck,@RepeatIntervalMinutes,
                                @MaxOccurrences,@Now);
                        """,new
                    {
                        DeliveryId=deliveryId,
                        MatchKey=MatchKey(match),SourceKey=deliverySourceKey,SourceType=content.SourceType??"order_submitted",
                        SourceId=content.SourceId,OwnerKey=match.OwnerKey,RuleId=match.RuleId,RuleType=match.RuleType,
                        RuleRevision=match.RuleRevision,Fingerprint=match.Fingerprint,IsBroadcast=match.IsBroadcast,
                        PopupMode=match.PopupMode,SoundId=match.SoundId,Priority=match.Priority,
                        ExpiresAfterMinutes=match.ExpiresAfterMinutes,RequiresAck=match.RequiresAck,
                        RepeatIntervalMinutes=match.IsBroadcast?match.RepeatIntervalMinutes:0,
                        MaxOccurrences=match.IsBroadcast?match.MaxOccurrences:1,Now=now
                    },tx,cancellationToken:ct));
                    if(match.IsBroadcast && match.RepeatIntervalMinutes>=1 && match.MaxOccurrences>1)
                    {
                        await connection.ExecuteAsync(new CommandDefinition("""
                            INSERT IGNORE INTO `NOTIFICATION_REPEAT_INSTANCES`
                                (`DELIVERY_ID`,`MATCH_KEY`,`SOURCE_KEY`,`SOURCE_TYPE`,`SOURCE_ID`,`EPISODE_ID`,
                                 `INTERVAL_MINUTES`,`OCCURRENCE_NO`,`MAX_OCCURRENCES`,`NEXT_AT`,`STATUS`,`CREATED_AT`,`UPDATED_AT`)
                            VALUES (@DeliveryId,@MatchKey,@SourceKey,@SourceType,@SourceId,@EpisodeId,
                                    @IntervalMinutes,1,@MaxOccurrences,@NextAt,'pending',@Now,@Now);
                            """,new
                        {
                            DeliveryId=deliveryId,MatchKey=MatchKey(match),SourceKey=deliverySourceKey,
                            SourceType=content.SourceType??"order_submitted",SourceId=content.SourceId,
                            EpisodeId=content.EpisodeId,IntervalMinutes=match.RepeatIntervalMinutes,
                            MaxOccurrences=match.MaxOccurrences,NextAt=row.CreatedAt.AddMinutes(match.RepeatIntervalMinutes),Now=now,
                        },tx,cancellationToken:ct));
                    }
                }
                if(deliveryChanged>0)
                    await AppendChangeAsync(connection,tx,now,delivery.AccountId,deliveryId,
                        content.OccurrenceNo>1?"notification.repeated":"notification.created",
                        new{deliveryId,occurrenceNo=content.OccurrenceNo,groupKey=content.GroupKey,episodeId=content.EpisodeId},ct);
            }
            await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_OUTBOX SET PROCESSED_AT=@Now,NEXT_ATTEMPT_AT=NULL WHERE ID=@Id;",new{Now=now,row.Id},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return true;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            throw;
        }
        catch(Exception ex)
        {
            if(outboxId is null) throw;
            await MarkOutboxFailureAsync(outboxId,ex,ct);
            return true;
        }
    }
    public async Task<bool> DispatchRepeatOneAsync(CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var now=clock.LocalDateTime;
        var repeat=await connection.QuerySingleOrDefaultAsync<RepeatInstanceRow>(new CommandDefinition("""
            SELECT `DELIVERY_ID` AS DeliveryId,`MATCH_KEY` AS MatchKey,`SOURCE_KEY` AS SourceKey,
                   `SOURCE_TYPE` AS SourceType,`SOURCE_ID` AS SourceId,`EPISODE_ID` AS EpisodeId,
                   `INTERVAL_MINUTES` AS IntervalMinutes,`OCCURRENCE_NO` AS OccurrenceNo,
                   `MAX_OCCURRENCES` AS MaxOccurrences,`NEXT_AT` AS NextAt,`STATUS` AS Status
            FROM `NOTIFICATION_REPEAT_INSTANCES`
            WHERE `STATUS`='pending' AND `NEXT_AT` IS NOT NULL AND `NEXT_AT`<=@Now
            ORDER BY `NEXT_AT`,`DELIVERY_ID`,`MATCH_KEY`
            LIMIT 1 FOR UPDATE SKIP LOCKED;
            """,new{Now=now},tx,cancellationToken:ct));
        if(repeat is null) return false;

        async Task<bool> StopAsync(string reason)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `NOTIFICATION_REPEAT_INSTANCES`
                SET `STATUS`='stopped',`STOP_REASON`=@Reason,`NEXT_AT`=NULL,`UPDATED_AT`=@Now
                WHERE `DELIVERY_ID`=@DeliveryId AND `MATCH_KEY`=@MatchKey AND `STATUS`='pending';
                """,new{Reason=reason,Now=now,repeat.DeliveryId,repeat.MatchKey},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return true;
        }
        if(repeat.IntervalMinutes<1||repeat.MaxOccurrences<2||repeat.OccurrenceNo>=repeat.MaxOccurrences)
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE `NOTIFICATION_REPEAT_INSTANCES` SET `STATUS`='completed',`NEXT_AT`=NULL,`UPDATED_AT`=@Now WHERE `DELIVERY_ID`=@DeliveryId AND `MATCH_KEY`=@MatchKey;",new{Now=now,repeat.DeliveryId,repeat.MatchKey},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return true;
        }
        var delivery=await connection.QuerySingleOrDefaultAsync<RepeatDeliveryRow>(new CommandDefinition("""
            SELECT `ID` AS Id,`SOURCE_KEY` AS SourceKey,`RECIPIENT_ACCOUNT_ID` AS RecipientAccountId,
                   `PAYLOAD_JSON` AS PayloadJson,`CREATED_AT` AS CreatedAt,`EXPIRES_AT` AS ExpiresAt,
                   `OCCURRENCE_NO` AS OccurrenceNo,`EPISODE_ID` AS EpisodeId,
                   `ACKNOWLEDGED_AT` AS AcknowledgedAt,`WITHDRAWN_AT` AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERIES` WHERE `ID`=@DeliveryId FOR UPDATE;
            """,new{repeat.DeliveryId},tx,cancellationToken:ct));
        if(delivery is null) return await StopAsync("delivery_missing");
        if(delivery.WithdrawnAt.HasValue||delivery.AcknowledgedAt.HasValue||delivery.ExpiresAt<=now)
            return await StopAsync(delivery.WithdrawnAt.HasValue?"withdrawn":delivery.AcknowledgedAt.HasValue?"acknowledged":"expired");
        var match=await connection.QuerySingleOrDefaultAsync<DeliveryMatchRow>(new CommandDefinition("""
            SELECT `DELIVERY_ID` AS DeliveryId,`MATCH_KEY` AS MatchKey,`SOURCE_KEY` AS SourceKey,
                   `SOURCE_TYPE` AS SourceType,`SOURCE_ID` AS SourceId,`OWNER_KEY` AS OwnerKey,
                   `RULE_ID` AS RuleId,`RULE_TYPE` AS RuleType,`RULE_REVISION` AS RuleRevision,
                   `RULE_FINGERPRINT` AS Fingerprint,`POPUP_MODE` AS PopupMode,`SOUND_ID` AS SoundId,
                   `PRIORITY` AS Priority,`EXPIRES_AFTER_MINUTES` AS ExpiresAfterMinutes,
                   `IS_BROADCAST` AS IsBroadcast,`REQUIRES_ACK` AS RequiresAck,
                   `REPEAT_INTERVAL_MINUTES` AS RepeatIntervalMinutes,`MAX_OCCURRENCES` AS MaxOccurrences,
                   `ACKNOWLEDGED_AT` AS AcknowledgedAt,`WITHDRAWN_AT` AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERY_MATCHES`
            WHERE `DELIVERY_ID`=@DeliveryId AND `MATCH_KEY`=@MatchKey FOR UPDATE;
            """,new{repeat.DeliveryId,repeat.MatchKey},tx,cancellationToken:ct));
        if(match is null||!match.IsBroadcast||match.WithdrawnAt.HasValue||match.AcknowledgedAt.HasValue)
            return await StopAsync(match is null?"match_missing":match.WithdrawnAt.HasValue?"withdrawn":"acknowledged");

        var settings=await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition(
            "SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM `NOTIFICATION_SETTINGS` WHERE OWNER_KEY='broadcast';",
            transaction:tx,cancellationToken:ct));
        var currentRule=settings is null?null:ReadRules(settings.RulesJson).FirstOrDefault(x=>x.Id==match.RuleId&&x.IsEnabled&&
            x.RuleRevision==match.RuleRevision&&string.Equals(x.Fingerprint,match.Fingerprint,StringComparison.Ordinal));
        if(currentRule is null) return await StopAsync("rule_changed");
        if(repeat.SourceType=="order_submitted" && !string.IsNullOrWhiteSpace(repeat.SourceId))
        {
            var orderActive=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT COUNT(*)>0 FROM `ORDERS`
                WHERE `ID`=@OrderId AND `ORDER_STATUS` IN ('submitted','partially_confirmed');
                """,new{OrderId=repeat.SourceId},tx,cancellationToken:ct));
            if(!orderActive) return await StopAsync("source_resolved");
        }
        if(repeat.SourceType=="order_backlog")
        {
            var episode=await connection.QuerySingleOrDefaultAsync<BacklogEpisodeRow>(new CommandDefinition("""
                SELECT `EPISODE_ID` AS EpisodeId,`RULE_ID` AS RuleId,`RULE_REVISION` AS RuleRevision,
                       `RULE_FINGERPRINT` AS RuleFingerprint,`THRESHOLD` AS Threshold,
                       `REQUIRED_DURATION_MINUTES` AS RequiredDurationMinutes,`PENDING_COUNT` AS PendingCount,
                       `FIRST_SEEN_AT` AS FirstSeenAt,`LAST_SEEN_AT` AS LastSeenAt,`ACTIVATED_AT` AS ActivatedAt,
                       `OCCURRENCE_NO` AS OccurrenceNo,`STATUS` AS Status
                FROM `NOTIFICATION_BACKLOG_EPISODES` WHERE `EPISODE_ID`=@EpisodeId FOR UPDATE;
                """,new{EpisodeId=repeat.EpisodeId??match.SourceId},tx,cancellationToken:ct));
            if(episode is null||episode.Status!="active") return await StopAsync("episode_resolved");
        }
        var nextOccurrence=repeat.OccurrenceNo+1;
        var currentContent=NormalizeMessage(JsonSerializer.Deserialize<MenuNotificationMessage>(delivery.PayloadJson,MenuPolicies.Json)??
            throw new BusinessException("通知重複內容格式無效。","NOTIFICATION_REPEAT_PAYLOAD_INVALID"));
        var repeatContent=currentContent with
        {
            OccurrenceNo=nextOccurrence,
            GroupKey=delivery.SourceKey,
            EpisodeId=repeat.EpisodeId??currentContent.EpisodeId,
            PresentationId=$"{delivery.SourceKey}:{nextOccurrence}",
        };
        var repeatSourceKey=$"broadcast:repeat:{MenuPolicies.Hash(new{repeat.DeliveryId,repeat.MatchKey,nextOccurrence})}";
        var repeatDeliveries=new[]{new PlannedDelivery(delivery.RecipientAccountId,repeatContent)};
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO `NOTIFICATION_OUTBOX` (`ID`,`SOURCE_KEY`,`PAYLOAD_JSON`,`CREATED_AT`)
            VALUES (@Id,@SourceKey,@Payload,@Now);
            """,new
        {
            Id=Guid.NewGuid().ToString(),SourceKey=repeatSourceKey,
            Payload=JsonSerializer.Serialize(repeatDeliveries,MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `NOTIFICATION_REPEAT_INSTANCES`
            SET `OCCURRENCE_NO`=@OccurrenceNo,
                `NEXT_AT`=CASE WHEN @OccurrenceNo>=`MAX_OCCURRENCES` THEN NULL ELSE @NextAt END,
                `STATUS`=CASE WHEN @OccurrenceNo>=`MAX_OCCURRENCES` THEN 'completed' ELSE 'pending' END,
                `UPDATED_AT`=@Now
            WHERE `DELIVERY_ID`=@DeliveryId AND `MATCH_KEY`=@MatchKey AND `STATUS`='pending';
            """,new
        {
            OccurrenceNo=nextOccurrence,NextAt=now.AddMinutes(repeat.IntervalMinutes),Now=now,
            repeat.DeliveryId,repeat.MatchKey,
        },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return true;
    }
    private static MenuNotificationMessage ComposeContent(MenuNotificationMessage normalized,IReadOnlyList<MenuNotificationRuleMatch> matches)
    {
        var broadcasts=matches.Where(x=>x.IsBroadcast)
            .OrderByDescending(x=>x.Priority=="high")
            .ThenByDescending(x=>x.RequiresAck)
            .ThenByDescending(x=>x.RuleType=="champagne_order_received")
            .ThenBy(x=>x.RuleId,StringComparer.Ordinal).ToArray();
        var requiresAck=broadcasts.Any(x=>x.RequiresAck);
        var popupMode=requiresAck?"critical_modal":broadcasts.Length>0?broadcasts[0].PopupMode:
            matches.Any(x=>x.PopupMode=="sticky")?"sticky":matches.Any(x=>x.PopupMode=="toast")?"toast":"none";
        var soundId=broadcasts.Select(x=>x.SoundId).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)) ??
            matches.Select(x=>x.SoundId).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
        return normalized with
        {
            IsBroadcast=broadcasts.Length>0,
            PopupMode=popupMode,
            SoundId=soundId,
            Priority=broadcasts.Any(x=>x.Priority=="high")?"high":"normal",
            ExpiresAfterMinutes=broadcasts.Select(x=>x.ExpiresAfterMinutes).DefaultIfEmpty(normalized.ExpiresAfterMinutes).Min(),
            RequiresAck=requiresAck,
            MatchedRules=matches.Select(x=>$"{(x.IsBroadcast?"broadcast":"personal")}:{x.RuleId}:{x.RuleRevision}").ToArray(),
            RuleMatches=matches.ToArray(),
        };
    }
    private static string MatchKey(MenuNotificationRuleMatch match)
        => MenuPolicies.Hash(new
        {
            match.OwnerKey,match.RuleId,match.RuleType,match.RuleRevision,match.Fingerprint,
            match.IsBroadcast,match.RequiresAck,
        });
    private static MenuNotificationRuleMatch[] ResolvePlannedMatches(IReadOnlyList<MenuNotificationRuleMatch> planned,IReadOnlyList<SettingsRow> settings)
    {
        var rulesByOwner=settings.ToDictionary(x=>x.OwnerKey,x=>ReadRules(x.RulesJson).ToDictionary(rule=>rule.Id,StringComparer.Ordinal),StringComparer.Ordinal);
        return planned.Where(match=>rulesByOwner.TryGetValue(match.OwnerKey,out var rules) &&
            rules.TryGetValue(match.RuleId,out var rule) && rule.IsEnabled && rule.RuleRevision==match.RuleRevision &&
            string.Equals(rule.Fingerprint,match.Fingerprint,StringComparison.Ordinal)).ToArray();
    }
    private static MenuNotificationRuleMatch[] ResolveLegacyMatches(MenuNotificationMessage content,IReadOnlyList<SettingsRow> settings)
        => settings.SelectMany(setting=>ReadRules(setting.RulesJson)
            .Where(rule=>rule.IsEnabled&&content.MatchedRules.Contains($"{(setting.OwnerKey=="broadcast"?"broadcast":"personal")}:{rule.Id}:{setting.Revision}"))
            .Select(rule=>new MenuNotificationRuleMatch(setting.OwnerKey,rule.Id,rule.RuleType,rule.RuleRevision,rule.Fingerprint,rule.PopupMode,rule.SoundId,setting.OwnerKey=="broadcast")
            {
                Priority=rule.Priority,ExpiresAfterMinutes=rule.ExpiresAfterMinutes,RequiresAck=rule.RequiresAck,
                RepeatIntervalMinutes=setting.OwnerKey=="broadcast"?rule.RepeatIntervalMinutes:0,
                MaxOccurrences=setting.OwnerKey=="broadcast"?rule.MaxOccurrences:1,
            })).ToArray();
    private async Task MarkOutboxFailureAsync(string outboxId,Exception error,CancellationToken ct)
    {
        const int maxAttempts=5;
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var row=await connection.QuerySingleOrDefaultAsync<OutboxFailureRow>(new CommandDefinition("SELECT ATTEMPTS AS Attempts FROM NOTIFICATION_OUTBOX WHERE ID=@Id AND PROCESSED_AT IS NULL FOR UPDATE;",new{Id=outboxId},tx,cancellationToken:ct));
        if(row is null){await tx.CommitAsync(ct);return;}
        var attempts=row.Attempts+1;
        var quarantined=attempts>=maxAttempts;
        var now=clock.LocalDateTime;
        var nextAttempt=quarantined?(DateTime?)null:now.AddSeconds(attempts switch {1=>5,2=>30,3=>120,4=>600,_=>1800});
        var lastError=error.Message.Length>1000?error.Message[..1000]:error.Message;
        await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_OUTBOX SET ATTEMPTS=@Attempts,NEXT_ATTEMPT_AT=@NextAttemptAt,LAST_ERROR=@LastError,IS_QUARANTINED=@Quarantined,QUARANTINED_AT=@QuarantinedAt WHERE ID=@Id AND PROCESSED_AT IS NULL;",new{Id=outboxId,Attempts=attempts,NextAttemptAt=nextAttempt,LastError=lastError,Quarantined=quarantined,QuarantinedAt=quarantined?now:(DateTime?)null},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@Id,'notification-worker',@Action,@Entity,@Before,@After,@Now);",new{Id=Guid.NewGuid().ToString(),Action=quarantined?"outbox.quarantined":"outbox.retry",Entity=outboxId,Before=JsonSerializer.Serialize(new{attempts=row.Attempts},MenuPolicies.Json),After=JsonSerializer.Serialize(new{attempts,error=lastError,nextAttempt,quarantined},MenuPolicies.Json),Now=now},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        logger.LogWarning("Notification outbox {OutboxId} failed at attempt {Attempt}; quarantined={Quarantined}.",outboxId,attempts,quarantined);
    }
    private static DateTimeOffset Taipei(DateTime value)=>new(value,TimeSpan.FromHours(8));
    private static MenuNotificationDelivery ToDelivery(DeliveryRow row)
    {
        var content=NormalizeMessage(JsonSerializer.Deserialize<MenuNotificationMessage>(row.PayloadJson,MenuPolicies.Json)??
            throw new BusinessException("通知內容格式無效。","NOTIFICATION_PAYLOAD_INVALID"));
        return new(row.Id,content,Taipei(row.CreatedAt),Taipei(row.ExpiresAt),
            row.ReadAt.HasValue?Taipei(row.ReadAt.Value):null,
            row.AcknowledgedAt.HasValue?Taipei(row.AcknowledgedAt.Value):null,
            row.WithdrawnAt.HasValue?Taipei(row.WithdrawnAt.Value):null);
    }
    private static string EncodeCursor(long sequence)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"notification:v1:{sequence}"));
    public static string CursorForSequence(long sequence)=>EncodeCursor(Math.Max(0,sequence));
    private static bool TryDecodeCursor(string? value,out long sequence)
    {
        sequence=0;
        if(string.IsNullOrWhiteSpace(value)) return true;
        try
        {
            var decoded=Encoding.UTF8.GetString(Convert.FromBase64String(value));
            var parts=decoded.Split(':',StringSplitOptions.RemoveEmptyEntries);
            return parts.Length==3&&parts[0]=="notification"&&parts[1]=="v1"&&long.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)&&sequence>=0;
        }
        catch(FormatException){return false;}
    }
    private static string EncodePageToken(DateTime createdAt,string id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{v=1,createdAtTicks=createdAt.Ticks,id},MenuPolicies.Json)));
    private static bool TryDecodePageToken(string? value,out DateTime createdAt,out string id)
    {
        createdAt=default;id="";
        if(string.IsNullOrWhiteSpace(value)) return true;
        try
        {
            using var document=JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
            if(document.RootElement.GetProperty("v").GetInt32()!=1) return false;
            createdAt=new(document.RootElement.GetProperty("createdAtTicks").GetInt64(),DateTimeKind.Unspecified);
            id=document.RootElement.GetProperty("id").GetString()??"";
            return id.Length>0;
        }
        catch(Exception ex) when(ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException){return false;}
    }
    private async Task<long> CurrentChangeSequenceAsync(MySqlConnection connection,MySqlTransaction? tx,CancellationToken ct)
        => await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(MAX(`SEQUENCE`),0) FROM `NOTIFICATION_CHANGES`;",transaction:tx,cancellationToken:ct));
    private static async Task AppendChangeAsync(MySqlConnection connection,MySqlTransaction tx,DateTime now,
        string account,string? deliveryId,string changeType,object payload,CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `NOTIFICATION_CHANGE_CURSOR`
            SET `NEXT_SEQUENCE`=`NEXT_SEQUENCE`+1,`UPDATED_AT`=@Now WHERE `ID`=1;
            """,new{Now=now},tx,cancellationToken:ct));
        var sequence=await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT `NEXT_SEQUENCE` FROM `NOTIFICATION_CHANGE_CURSOR` WHERE `ID`=1 FOR UPDATE;",transaction:tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `NOTIFICATION_CHANGES`
                (`SEQUENCE`,`CHANGE_ID`,`RECIPIENT_ACCOUNT_ID`,`DELIVERY_ID`,`CHANGE_TYPE`,`PAYLOAD_JSON`,`CREATED_AT`)
            VALUES (@Sequence,@ChangeId,@Account,@DeliveryId,@ChangeType,@Payload,@Now);
            """,new
        {
            Sequence=sequence,ChangeId=Guid.NewGuid().ToString(),Account=account,DeliveryId=deliveryId,
            ChangeType=changeType,Payload=JsonSerializer.Serialize(payload,MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
    }
    public async Task<MenuNotificationInbox> InboxAsync(ClaimsPrincipal user,CancellationToken ct,
        string? pageToken=null,int limit=100)
    {
        limit=Math.Clamp(limit,1,100);
        var account=Account(user);var now=clock.LocalDateTime;
        if(!TryDecodePageToken(pageToken,out var beforeCreatedAt,out var beforeId))
            throw new BusinessException("通知分頁游標格式無效。","NOTIFICATION_PAGE_TOKEN_INVALID");
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var since=now.AddDays(-30);
        if(!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM ADMIN_USERS WHERE ID=@Account AND IS_ACTIVE=TRUE;",new{Account=account},tx,cancellationToken:ct))) throw new UnauthorizedAccessException();
        var expired=(await connection.QueryAsync<DeliveryRow>(new CommandDefinition("""
            SELECT D.ID AS Id,D.PAYLOAD_JSON AS PayloadJson,D.CREATED_AT AS CreatedAt,D.EXPIRES_AT AS ExpiresAt,
                   D.READ_AT AS ReadAt,D.ACKNOWLEDGED_AT AS AcknowledgedAt,D.WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERIES` D
            LEFT JOIN `ORDERS` O ON O.ID=JSON_UNQUOTE(JSON_EXTRACT(D.PAYLOAD_JSON,'$.orderId'))
            WHERE D.RECIPIENT_ACCOUNT_ID=@Account AND D.EXPIRES_AT>@Now
              AND COALESCE(JSON_UNQUOTE(JSON_EXTRACT(D.PAYLOAD_JSON,'$.sourceType')),'order_submitted')='order_submitted'
              AND (O.ID IS NULL OR O.ORDER_STATUS IN ('cancelled','canceled','expired','completed'))
            FOR UPDATE;
            """,new{Account=account,Now=now},tx,cancellationToken:ct))).ToArray();
        if(expired.Length>0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `NOTIFICATION_DELIVERIES` D
                LEFT JOIN `ORDERS` O ON O.ID=JSON_UNQUOTE(JSON_EXTRACT(D.PAYLOAD_JSON,'$.orderId'))
                SET D.EXPIRES_AT=@Now
                WHERE D.RECIPIENT_ACCOUNT_ID=@Account AND D.EXPIRES_AT>@Now
                  AND COALESCE(JSON_UNQUOTE(JSON_EXTRACT(D.PAYLOAD_JSON,'$.sourceType')),'order_submitted')='order_submitted'
                  AND (O.ID IS NULL OR O.ORDER_STATUS IN ('cancelled','canceled','expired','completed'));
                """,new{Account=account,Now=now},tx,cancellationToken:ct));
            foreach(var row in expired)
                await AppendChangeAsync(connection,tx,now,account,row.Id,"notification.expired",new{deliveryId=row.Id,expiresAt=now},ct);
        }
        await tx.CommitAsync(ct);
        var args=new
        {
            Account=account,Since=since,Now=now,Limit=limit+1,
            BeforeCreatedAt=string.IsNullOrWhiteSpace(pageToken)?(DateTime?)null:beforeCreatedAt,
            BeforeId=string.IsNullOrWhiteSpace(pageToken)?null:beforeId,
        };
        var rows=(await connection.QueryAsync<DeliveryRow>(new CommandDefinition("""
            SELECT ID AS Id,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt,EXPIRES_AT AS ExpiresAt,
                   READ_AT AS ReadAt,ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERIES`
            WHERE RECIPIENT_ACCOUNT_ID=@Account AND CREATED_AT>=@Since
              AND (@BeforeCreatedAt IS NULL OR CREATED_AT<@BeforeCreatedAt OR (CREATED_AT=@BeforeCreatedAt AND ID<@BeforeId))
            ORDER BY CREATED_AT DESC,ID DESC LIMIT @Limit;
            """,args,cancellationToken:ct))).ToArray();
        var hasMore=rows.Length>limit;var visible=rows.Take(limit).ToArray();
        var unread=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM NOTIFICATION_DELIVERIES WHERE RECIPIENT_ACCOUNT_ID=@Account AND CREATED_AT>=@Since AND EXPIRES_AT>@Now AND WITHDRAWN_AT IS NULL AND READ_AT IS NULL;",new{Account=account,Since=since,Now=now},cancellationToken:ct));
        var cursor=await CurrentChangeSequenceAsync(connection,null,ct);
        var next=hasMore&&visible.Length>0?EncodePageToken(visible[^1].CreatedAt,visible[^1].Id):null;
        return new(visible.Select(ToDelivery).ToArray(),unread)
        {
            SnapshotCursor=EncodeCursor(cursor),NextPageToken=next,HasMore=hasMore,
        };
    }
    public async Task<MenuNotificationChangePage> ChangesAsync(ClaimsPrincipal user,string? cursor,
        int limit,CancellationToken ct)
    {
        limit=Math.Clamp(limit<=0?100:limit,1,100);
        var account=Account(user);
        if(!TryDecodeCursor(cursor,out var from))
            throw new BusinessException("通知增量游標格式無效。","NOTIFICATION_CURSOR_INVALID");
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        if(!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM ADMIN_USERS WHERE ID=@Account AND IS_ACTIVE=TRUE;",new{Account=account},cancellationToken:ct))) throw new UnauthorizedAccessException();
        var current=await CurrentChangeSequenceAsync(connection,null,ct);
        if(from>current) throw new BusinessException("通知增量游標超前，請重新同步。","NOTIFICATION_CURSOR_INVALID");
        var min=await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT MIN(`SEQUENCE`) FROM `NOTIFICATION_CHANGES` WHERE `RECIPIENT_ACCOUNT_ID`=@Account;",new{Account=account},cancellationToken:ct));
        if(from>0&&min.HasValue&&from<min.Value-1)
            return new([],EncodeCursor(current),true,"cursor_expired");
        var rows=(await connection.QueryAsync<ChangeRow>(new CommandDefinition("""
            SELECT `SEQUENCE` AS Sequence,`CHANGE_ID` AS ChangeId,`DELIVERY_ID` AS DeliveryId,
                   `CHANGE_TYPE` AS ChangeType,`CREATED_AT` AS CreatedAt
            FROM `NOTIFICATION_CHANGES`
            WHERE `RECIPIENT_ACCOUNT_ID`=@Account AND `SEQUENCE`>@From
            ORDER BY `SEQUENCE` LIMIT @Limit;
            """,new{Account=account,From=from,Limit=limit},cancellationToken:ct))).ToArray();
        var deliveryIds=rows.Where(x=>x.DeliveryId is not null).Select(x=>x.DeliveryId!).Distinct(StringComparer.Ordinal).ToArray();
        var deliveries=deliveryIds.Length==0?Array.Empty<DeliveryRow>(): (await connection.QueryAsync<DeliveryRow>(new CommandDefinition("""
            SELECT ID AS Id,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt,EXPIRES_AT AS ExpiresAt,
                   READ_AT AS ReadAt,ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERIES` WHERE RECIPIENT_ACCOUNT_ID=@Account AND ID IN @Ids;
            """,new{Account=account,Ids=deliveryIds},cancellationToken:ct))).ToArray();
        var byId=deliveries.ToDictionary(x=>x.Id,ToDelivery,StringComparer.Ordinal);
        var items=rows.Select(row=>new MenuNotificationChange(row.Sequence,row.ChangeId,row.ChangeType,row.DeliveryId,
            row.DeliveryId is not null&&byId.TryGetValue(row.DeliveryId,out var delivery)?delivery:null,Taipei(row.CreatedAt))).ToArray();
        var next=rows.Length==0?from:rows[^1].Sequence;
        return new(items,EncodeCursor(next),false,null);
    }
    public async Task ReadAsync(ClaimsPrincipal user,string[] ids,CancellationToken ct)
    {
        if(ids is null || ids.Length>100) throw new BusinessException("一次最多標記 100 筆。","NOTIFICATION_READ_INVALID");
        if(ids.Length==0) return;
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var account=Account(user);var now=clock.LocalDateTime;
        if(!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM ADMIN_USERS WHERE ID=@Account AND IS_ACTIVE=TRUE;",new{Account=account},tx,cancellationToken:ct)))
            throw new UnauthorizedAccessException();
        var unreadIds=(await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT ID FROM NOTIFICATION_DELIVERIES WHERE RECIPIENT_ACCOUNT_ID=@Account AND ID IN @Ids AND READ_AT IS NULL FOR UPDATE;",
            new{Account=account,Ids=ids},tx,cancellationToken:ct))).ToArray();
        var changed=await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_DELIVERIES SET READ_AT=COALESCE(READ_AT,@Now) WHERE RECIPIENT_ACCOUNT_ID=@Account AND ID IN @Ids;",new{Now=now,Account=account,Ids=ids},tx,cancellationToken:ct));
        foreach(var id in unreadIds)
            await AppendChangeAsync(connection,tx,now,account,id,"notification.read",new{deliveryId=id,readAt=now},ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@Id,@Actor,'notification.read',@Entity,NULL,@After,@Now);",new
        {
            Id=Guid.NewGuid().ToString(),Actor=account,Entity=account,
            After=JsonSerializer.Serialize(new{ids,changed},MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }
    public async Task<NotificationAcknowledgementResult> AcknowledgeAsync(ClaimsPrincipal user,string deliveryId,CancellationToken ct)
    {
        var account=Account(user);
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var now=clock.LocalDateTime;
        var delivery=await connection.QuerySingleOrDefaultAsync<AcknowledgeDeliveryRow>(new CommandDefinition("""
            SELECT ID AS Id,EXPIRES_AT AS ExpiresAt,ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERIES`
            WHERE ID=@DeliveryId AND RECIPIENT_ACCOUNT_ID=@Account
            FOR UPDATE;
            """,new{DeliveryId=deliveryId,Account=account},tx,cancellationToken:ct));
        if(delivery is null) throw new BusinessException("找不到這筆通知。","NOTIFICATION_DELIVERY_NOT_FOUND");
        if(delivery.WithdrawnAt.HasValue||delivery.ExpiresAt<=now)
            throw new BusinessException("這筆通知已撤回或過期，無法新增已知道紀錄。","NOTIFICATION_ACK_UNAVAILABLE");
        var matches=(await connection.QueryAsync<DeliveryMatchRow>(new CommandDefinition("""
            SELECT DELIVERY_ID AS DeliveryId,MATCH_KEY AS MatchKey,SOURCE_KEY AS SourceKey,
                   SOURCE_TYPE AS SourceType,SOURCE_ID AS SourceId,OWNER_KEY AS OwnerKey,RULE_ID AS RuleId,
                   RULE_TYPE AS RuleType,RULE_REVISION AS RuleRevision,RULE_FINGERPRINT AS Fingerprint,
                   POPUP_MODE AS PopupMode,SOUND_ID AS SoundId,PRIORITY AS Priority,
                   EXPIRES_AFTER_MINUTES AS ExpiresAfterMinutes,
                   IS_BROADCAST AS IsBroadcast,REQUIRES_ACK AS RequiresAck,
                   REPEAT_INTERVAL_MINUTES AS RepeatIntervalMinutes,MAX_OCCURRENCES AS MaxOccurrences,
                   ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERY_MATCHES`
            WHERE DELIVERY_ID=@DeliveryId
            FOR UPDATE;
            """,new{DeliveryId=deliveryId},tx,cancellationToken:ct))).ToArray();
        var required=matches.Where(x=>x.RequiresAck&&!x.WithdrawnAt.HasValue).ToArray();
        if(required.Length==0) throw new BusinessException("這筆通知不需要逐人已知道。","NOTIFICATION_ACK_NOT_REQUIRED");
        if(required.All(x=>x.AcknowledgedAt.HasValue))
        {
            await tx.CommitAsync(ct);
            return new(deliveryId,true,0);
        }
        var changed=await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `NOTIFICATION_DELIVERY_MATCHES`
            SET `ACKNOWLEDGED_AT`=@Now
            WHERE `DELIVERY_ID`=@DeliveryId AND `REQUIRES_ACK`=TRUE
              AND `ACKNOWLEDGED_AT` IS NULL AND `WITHDRAWN_AT` IS NULL;
            """,new{DeliveryId=deliveryId,Now=now},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("UPDATE `NOTIFICATION_DELIVERIES` SET `ACKNOWLEDGED_AT`=COALESCE(`ACKNOWLEDGED_AT`,@Now) WHERE `ID`=@DeliveryId;",new{DeliveryId=deliveryId,Now=now},tx,cancellationToken:ct));
        if(changed>0)
            await AppendChangeAsync(connection,tx,now,account,deliveryId,"notification.acknowledged",new{deliveryId,acknowledgedAt=now},ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `NOTIFICATION_AUDIT`
                (`ID`,`ACTOR_ACCOUNT_ID`,`ACTION_TYPE`,`ENTITY_ID`,`BEFORE_JSON`,`AFTER_JSON`,`CREATED_AT`)
            VALUES (@Id,@Actor,'notification.acknowledged',@Entity,@Before,@After,@Now);
            """,new
        {
            Id=Guid.NewGuid().ToString(),Actor=account,Entity=deliveryId,
            Before=JsonSerializer.Serialize(new{acknowledgedAt=delivery.AcknowledgedAt},MenuPolicies.Json),
            After=JsonSerializer.Serialize(new{acknowledgedAt=now,matchCount=changed},MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(deliveryId,false,changed);
    }
    public async Task<BroadcastWithdrawResult> WithdrawBroadcastAsync(ClaimsPrincipal user,string broadcastId,CancellationToken ct)
    {
        _=Owner(user,true);
        var normalizedId=(broadcastId??"").Trim();
        if(normalizedId.Length==0||normalizedId.Length>100) throw new BusinessException("廣播實例識別碼無效。","BROADCAST_ID_INVALID");
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var now=clock.LocalDateTime;
        var matched=(await connection.QueryAsync<DeliveryMatchRow>(new CommandDefinition("""
            SELECT DELIVERY_ID AS DeliveryId,MATCH_KEY AS MatchKey,SOURCE_KEY AS SourceKey,
                   SOURCE_TYPE AS SourceType,SOURCE_ID AS SourceId,OWNER_KEY AS OwnerKey,RULE_ID AS RuleId,
                   RULE_TYPE AS RuleType,RULE_REVISION AS RuleRevision,RULE_FINGERPRINT AS Fingerprint,
                   POPUP_MODE AS PopupMode,SOUND_ID AS SoundId,PRIORITY AS Priority,
                   EXPIRES_AFTER_MINUTES AS ExpiresAfterMinutes,
                   IS_BROADCAST AS IsBroadcast,REQUIRES_ACK AS RequiresAck,
                   REPEAT_INTERVAL_MINUTES AS RepeatIntervalMinutes,MAX_OCCURRENCES AS MaxOccurrences,
                   ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
            FROM `NOTIFICATION_DELIVERY_MATCHES`
            WHERE SOURCE_ID=@BroadcastId AND IS_BROADCAST=TRUE
            FOR UPDATE;
            """,new{BroadcastId=normalizedId},tx,cancellationToken:ct))).ToArray();
        if(matched.Length==0) throw new BusinessException("找不到可撤回的廣播實例。","BROADCAST_NOT_FOUND");
        var pending=matched.Count(x=>!x.WithdrawnAt.HasValue);
        if(pending==0)
        {
            await tx.CommitAsync(ct);
            return new(normalizedId,true,0);
        }
        await connection.ExecuteAsync(new CommandDefinition("UPDATE `NOTIFICATION_DELIVERY_MATCHES` SET `WITHDRAWN_AT`=@Now WHERE SOURCE_ID=@BroadcastId AND IS_BROADCAST=TRUE AND WITHDRAWN_AT IS NULL;",new{BroadcastId=normalizedId,Now=now},tx,cancellationToken:ct));
        var deliveryIds=matched.Select(x=>x.DeliveryId).Distinct(StringComparer.Ordinal).ToArray();
        foreach(var deliveryId in deliveryIds)
        {
            var delivery=await connection.QuerySingleAsync<DeliveryStateRow>(new CommandDefinition("SELECT ID AS Id,RECIPIENT_ACCOUNT_ID AS RecipientAccountId,PAYLOAD_JSON AS PayloadJson,ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt FROM `NOTIFICATION_DELIVERIES` WHERE ID=@DeliveryId FOR UPDATE;",new{DeliveryId=deliveryId},tx,cancellationToken:ct));
            var current=(await connection.QueryAsync<DeliveryMatchRow>(new CommandDefinition("""
                SELECT DELIVERY_ID AS DeliveryId,MATCH_KEY AS MatchKey,SOURCE_KEY AS SourceKey,
                       SOURCE_TYPE AS SourceType,SOURCE_ID AS SourceId,OWNER_KEY AS OwnerKey,RULE_ID AS RuleId,
                       RULE_TYPE AS RuleType,RULE_REVISION AS RuleRevision,RULE_FINGERPRINT AS Fingerprint,
                       POPUP_MODE AS PopupMode,SOUND_ID AS SoundId,PRIORITY AS Priority,
                       EXPIRES_AFTER_MINUTES AS ExpiresAfterMinutes,
                       IS_BROADCAST AS IsBroadcast,REQUIRES_ACK AS RequiresAck,
                       REPEAT_INTERVAL_MINUTES AS RepeatIntervalMinutes,MAX_OCCURRENCES AS MaxOccurrences,
                       ACKNOWLEDGED_AT AS AcknowledgedAt,WITHDRAWN_AT AS WithdrawnAt
                FROM `NOTIFICATION_DELIVERY_MATCHES` WHERE DELIVERY_ID=@DeliveryId;
                """,new{DeliveryId=deliveryId},tx,cancellationToken:ct))).ToArray();
            var active=current.Where(x=>!x.WithdrawnAt.HasValue).ToArray();
            var content=ComposeContent(NormalizeMessage(JsonSerializer.Deserialize<MenuNotificationMessage>(delivery.PayloadJson,MenuPolicies.Json)!),active.Select(ToRuleMatch).ToArray());
            var activeRequired=active.Where(x=>x.RequiresAck).ToArray();
            var acknowledgedAt=activeRequired.Length>0&&activeRequired.All(x=>x.AcknowledgedAt.HasValue)
                ? activeRequired.Max(x=>x.AcknowledgedAt) : activeRequired.Length==0?delivery.AcknowledgedAt:null;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `NOTIFICATION_DELIVERIES`
                SET `PAYLOAD_JSON`=@Payload,`ACKNOWLEDGED_AT`=@AcknowledgedAt,
                    `WITHDRAWN_AT`=@WithdrawnAt
                WHERE ID=@DeliveryId;
                """,new
            {
                DeliveryId=deliveryId,Payload=JsonSerializer.Serialize(content,MenuPolicies.Json),
                AcknowledgedAt=acknowledgedAt,WithdrawnAt=active.Length==0?now:(DateTime?)null,
            },tx,cancellationToken:ct));
            await AppendChangeAsync(connection,tx,now,delivery.RecipientAccountId,deliveryId,
                active.Length==0?"notification.withdrawn":"notification.updated",
                new{deliveryId,withdrawnAt=active.Length==0?now:(DateTime?)null,sourceId=normalizedId},ct);
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `NOTIFICATION_AUDIT`
                (`ID`,`ACTOR_ACCOUNT_ID`,`ACTION_TYPE`,`ENTITY_ID`,`BEFORE_JSON`,`AFTER_JSON`,`CREATED_AT`)
            VALUES (@Id,@Actor,'broadcast.withdrawn',@Entity,@Before,@After,@Now);
            """,new
        {
            Id=Guid.NewGuid().ToString(),Actor=Account(user),Entity=normalizedId,
            Before=JsonSerializer.Serialize(new{matchedCount=matched.Length,pendingCount=pending},MenuPolicies.Json),
            After=JsonSerializer.Serialize(new{affectedDeliveryCount=deliveryIds.Length,withdrawnAt=now},MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(normalizedId,false,deliveryIds.Length);
    }
    public async Task<BroadcastReceiptSummary> BroadcastReceiptsAsync(ClaimsPrincipal user,string broadcastId,CancellationToken ct)
    {
        _=Owner(user,true);
        var normalizedId=(broadcastId??"").Trim();
        if(normalizedId.Length==0||normalizedId.Length>100) throw new BusinessException("廣播實例識別碼無效。","BROADCAST_ID_INVALID");
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        var now=clock.LocalDateTime;
        var rows=(await connection.QueryAsync<ReceiptRow>(new CommandDefinition("""
            SELECT D.ID AS Id,D.READ_AT AS ReadAt,D.EXPIRES_AT AS ExpiresAt,
                   MAX(CASE WHEN M.REQUIRES_ACK=TRUE THEN 1 ELSE 0 END) AS HasAck,
                   MAX(CASE WHEN M.REQUIRES_ACK=TRUE AND M.ACKNOWLEDGED_AT IS NOT NULL THEN 1 ELSE 0 END) AS HasAcknowledged,
                   MAX(CASE WHEN M.REQUIRES_ACK=TRUE AND M.ACKNOWLEDGED_AT IS NULL AND M.WITHDRAWN_AT IS NULL THEN 1 ELSE 0 END) AS HasPendingAck,
                   MAX(CASE WHEN M.WITHDRAWN_AT IS NOT NULL THEN 1 ELSE 0 END) AS IsWithdrawn
            FROM `NOTIFICATION_DELIVERY_MATCHES` M
            JOIN `NOTIFICATION_DELIVERIES` D ON D.ID=M.DELIVERY_ID
            WHERE M.SOURCE_ID=@BroadcastId AND M.IS_BROADCAST=TRUE
            GROUP BY D.ID,D.READ_AT,D.EXPIRES_AT;
            """,new{BroadcastId=normalizedId},cancellationToken:ct))).ToArray();
        if(rows.Length==0) throw new BusinessException("找不到這個廣播實例的確認進度。","BROADCAST_NOT_FOUND");
        var pending=rows.Count(x=>x.HasPendingAck&&x.ExpiresAt>now);
        var expired=rows.Count(x=>x.HasPendingAck&&x.ExpiresAt<=now);
        return new(normalizedId,rows.Length,
            rows.Count(x=>!x.ReadAt.HasValue&&x.ExpiresAt>now&&!x.IsWithdrawn),
            rows.Count(x=>x.ReadAt.HasValue),
            rows.Count(x=>x.HasAck&&!x.HasPendingAck),pending,expired,
            rows.Count(x=>x.IsWithdrawn));
    }
    private static MenuNotificationRuleMatch ToRuleMatch(DeliveryMatchRow row)
        => new(row.OwnerKey,row.RuleId,row.RuleType,row.RuleRevision,row.Fingerprint,
            row.PopupMode,row.SoundId,row.IsBroadcast)
        {
            Priority=row.Priority,ExpiresAfterMinutes=row.ExpiresAfterMinutes,
            RequiresAck=row.RequiresAck,
            RepeatIntervalMinutes=row.RepeatIntervalMinutes,
            MaxOccurrences=row.MaxOccurrences,
        };
    private static MenuNotificationRule NormalizeRule(MenuNotificationRule rule,string? ownerStaffId)
    {
        var ruleType=rule.RuleType?.Trim().ToLowerInvariant()??"";
        var targetMode=string.IsNullOrWhiteSpace(rule.TargetMode)?null:rule.TargetMode.Trim().ToLowerInvariant();
        var targetStaffId=string.IsNullOrWhiteSpace(rule.TargetStaffId)?null:rule.TargetStaffId.Trim();
        if(targetMode=="none") targetMode=null;
        if(targetMode=="staff" && targetStaffId is not null && ownerStaffId is not null &&
            string.Equals(targetStaffId,ownerStaffId,StringComparison.Ordinal))
        {
            targetMode="self";
            targetStaffId=null;
        }
        var audienceMode=string.IsNullOrWhiteSpace(rule.AudienceMode)?"all":rule.AudienceMode.Trim().ToLowerInvariant();
        var audienceStaffIds=(rule.AudienceStaffIds??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var audienceRoles=(rule.AudienceRoles??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var priority=string.IsNullOrWhiteSpace(rule.Priority)?"normal":rule.Priority.Trim().ToLowerInvariant();
        var expiresAfterMinutes=rule.ExpiresAfterMinutes<=0?15:rule.ExpiresAfterMinutes;
        var repeatIntervalMinutes=rule.RepeatIntervalMinutes<=0?0:Math.Clamp(rule.RepeatIntervalMinutes,1,1440);
        var maxOccurrences=Math.Clamp(rule.MaxOccurrences<=0?1:rule.MaxOccurrences,1,5);
        if(repeatIntervalMinutes==0) maxOccurrences=1;
        var backlogThreshold=Math.Max(1,rule.BacklogThreshold<=0?1:rule.BacklogThreshold);
        var backlogDurationMinutes=Math.Clamp(rule.BacklogDurationMinutes<=0?5:rule.BacklogDurationMinutes,1,1440);
        var normalized=rule with
        {
            Name=string.IsNullOrWhiteSpace(rule.Name)?DefaultRuleName(ruleType):rule.Name.Trim(),
            RuleType=ruleType,
            PopupMode=rule.PopupMode?.Trim().ToLowerInvariant()??"",
            TargetMode=targetMode,
            TargetStaffId=targetStaffId,
            AudienceMode=audienceMode,
            AudienceStaffIds=audienceStaffIds,
            AudienceRoles=audienceRoles,
            Priority=priority,
            ExpiresAfterMinutes=expiresAfterMinutes,
            RepeatIntervalMinutes=repeatIntervalMinutes,
            MaxOccurrences=maxOccurrences,
            BacklogThreshold=backlogThreshold,
            BacklogDurationMinutes=backlogDurationMinutes,
            TemplateCode=string.IsNullOrWhiteSpace(rule.TemplateCode)?ruleType:rule.TemplateCode.Trim().ToLowerInvariant(),
            RequiresAck=false,
            Fingerprint="",
        };
        return normalized with {Fingerprint=CalculateFingerprint(normalized)};
    }
    private static string DefaultRuleName(string ruleType)
        => ruleType switch
        {
            "champagne_order_received"=>"香檳塔新訂單",
            "order_received"=>"新點餐訂單",
            "order_backlog"=>"待處理訂單堆積",
            _=>"店內廣播規則",
        };
    private static MenuNotificationRule NormalizeStoredRule(MenuNotificationRule rule,string? ownerStaffId)
    {
        var normalized=NormalizeRule(rule,ownerStaffId);
        return normalized with
        {
            SchemaVersion=NotificationSchemaVersion,
            RuleRevision=normalized.RuleRevision<=0?1:normalized.RuleRevision,
        };
    }
    private static bool HasVersionedFields(MenuNotificationRule rule)
        => rule.SchemaVersion>=NotificationSchemaVersion || rule.RuleRevision>0 || !string.IsNullOrWhiteSpace(rule.Fingerprint) ||
           rule.TargetMode is not null || rule.TargetStaffId is not null || rule.OffsetMinutes.HasValue ||
           !string.IsNullOrWhiteSpace(rule.Name) || rule.AudienceMode!="all" || rule.AudienceStaffIds?.Length>0 ||
           rule.AudienceRoles?.Length>0 || rule.Priority!="normal" || rule.ExpiresAfterMinutes!=15 ||
           !string.IsNullOrWhiteSpace(rule.TemplateCode) || rule.RequiresAck ||
           rule.RepeatIntervalMinutes>0 || rule.MaxOccurrences>1 || rule.RuleType=="order_backlog" ||
           rule.BacklogThreshold!=1 || rule.BacklogDurationMinutes!=5;
    private static string CalculateFingerprint(MenuNotificationRule rule)
    {
        // Keep the S02/S07 fingerprint byte-for-byte compatible for personal
        // rules and legacy all-audience broadcast rules. Audience fields only
        // become part of the trigger identity when a scoped broadcast uses them.
        var hasRepeatFields=rule.RepeatIntervalMinutes>0 || rule.MaxOccurrences>1 || rule.RuleType=="order_backlog" ||
            rule.BacklogThreshold!=1 || rule.BacklogDurationMinutes!=5;
        if(rule.AudienceMode=="all"&&(rule.AudienceStaffIds?.Length??0)==0&&(rule.AudienceRoles?.Length??0)==0 && !hasRepeatFields)
            return MenuPolicies.Hash(new
            {
                ruleType=rule.RuleType,
                targetMode=rule.TargetMode??"",
                targetStaffId=rule.TargetStaffId??"",
                offsetMinutes=rule.OffsetMinutes,
            });
        return MenuPolicies.Hash(new
        {
            ruleType=rule.RuleType,
            targetMode=rule.TargetMode??"",
            targetStaffId=rule.TargetStaffId??"",
            offsetMinutes=rule.OffsetMinutes,
            audienceMode=rule.AudienceMode,
            audienceStaffIds=rule.AudienceStaffIds,
            audienceRoles=rule.AudienceRoles,
            repeatIntervalMinutes=rule.RepeatIntervalMinutes,
            maxOccurrences=rule.MaxOccurrences,
            backlogThreshold=rule.BacklogThreshold,
            backlogDurationMinutes=rule.BacklogDurationMinutes,
        });
    }
    private static void ValidateRuleConditions(MenuNotificationRule rule)
    {
        var hasTarget=rule.TargetMode is not null || rule.TargetStaffId is not null;
        var targetValid=rule.TargetMode is "self" or "staff" or "all" &&
            (rule.TargetMode is not "staff" || !string.IsNullOrWhiteSpace(rule.TargetStaffId)) &&
            (rule.TargetMode is "staff" || rule.TargetStaffId is null);
        var offsetValid=!rule.OffsetMinutes.HasValue || rule.OffsetMinutes.Value is >=0 and <=1440;
        switch(rule.RuleType)
        {
            case "order_received":
            case "champagne_order_received":
                if(hasTarget || rule.OffsetMinutes.HasValue) throw InvalidRuleConditions();
                return;
            case "order_backlog":
                if(hasTarget || rule.OffsetMinutes.HasValue) throw InvalidRuleConditions();
                return;
            case "designated_order_received":
                if(!targetValid || !hasTarget || rule.OffsetMinutes.HasValue) throw InvalidRuleConditions();
                return;
            case "nomination_starting":
            case "nomination_ending":
            case "nomination_ended":
                if(!targetValid || !hasTarget || !rule.OffsetMinutes.HasValue || !offsetValid) throw InvalidRuleConditions();
                return;
            case "business_opening_soon":
            case "business_closing_soon":
                if(hasTarget || !rule.OffsetMinutes.HasValue || !offsetValid) throw InvalidRuleConditions();
                return;
            default:
                throw InvalidRuleConditions();
        }
    }
    private static void ValidateBroadcastRule(MenuNotificationRule rule)
    {
        if(!BroadcastAudienceModes.Contains(rule.AudienceMode,StringComparer.Ordinal) ||
           !BroadcastPriorities.Contains(rule.Priority,StringComparer.Ordinal) ||
           rule.ExpiresAfterMinutes is <1 or >1440 ||
           rule.RepeatIntervalMinutes is <0 or >1440 ||
           rule.MaxOccurrences is <1 or >5 ||
           (rule.RepeatIntervalMinutes==0 && rule.MaxOccurrences!=1) ||
           (rule.RuleType=="order_backlog" && (rule.BacklogThreshold<1 || rule.BacklogDurationMinutes is <1 or >1440)) ||
           (rule.RuleType!="order_backlog" && (rule.BacklogThreshold!=1 || rule.BacklogDurationMinutes!=5)) ||
           (rule.RequiresAck && rule.PopupMode!="critical_modal") ||
           rule.TemplateCode is not ("order_received" or "champagne_order_received" or "order_backlog") ||
           rule.AudienceRoles.Any(role=>!BroadcastRoles.Contains(role,StringComparer.Ordinal)) ||
           (rule.AudienceMode=="roles"&&rule.AudienceRoles.Length==0) ||
           (rule.AudienceMode=="staff"&&rule.AudienceStaffIds.Length==0) ||
           (rule.AudienceMode is not "roles" and not "staff" && (rule.AudienceRoles.Length>0||rule.AudienceStaffIds.Length>0)))
            throw new BusinessException("店內廣播的受眾、優先級、模板或有效期參數無效。", "BROADCAST_RULE_INVALID");
    }
    private static BusinessException InvalidRuleConditions()
        => new("通知規則的監看對象與分鐘條件不符合該規則類型。N 必須是 0–1,440 的整數。", "NOTIFICATION_RULE_CONDITION_INVALID");
    private static MenuNotificationRule[] ReadRules(string? json)
        => ReadSettingsDocument(json).Rules.Select(rule=>NormalizeStoredRule(rule,null)).ToArray();
    private static NotificationSettingsDocument ReadSettingsDocument(string? json)
    {
        if(string.IsNullOrWhiteSpace(json)) return new(1,[]);
        try
        {
            using var document=JsonDocument.Parse(json);
            if(document.RootElement.ValueKind==JsonValueKind.Array)
                return new(1,document.RootElement.Deserialize<MenuNotificationRule[]>(MenuPolicies.Json)??[]);
            if(document.RootElement.ValueKind==JsonValueKind.Object)
            {
                var value=document.RootElement.Deserialize<NotificationSettingsDocument>(MenuPolicies.Json)??new(1,[]);
                return new(value.SchemaVersion<=0?1:value.SchemaVersion,value.Rules??[]);
            }
        }
        catch(JsonException)
        {
            throw new BusinessException("通知設定資料格式無效，請重新載入後再試。", "NOTIFICATION_SETTINGS_INVALID");
        }
        throw new BusinessException("通知設定資料格式無效，請重新載入後再試。", "NOTIFICATION_SETTINGS_INVALID");
    }
    private static MenuNotificationMessage NormalizeMessage(MenuNotificationMessage message)
    {
        var sourceId=string.IsNullOrWhiteSpace(message.SourceId)?message.OrderId:message.SourceId;
        var groupKey=string.IsNullOrWhiteSpace(message.GroupKey)?sourceId:message.GroupKey;
        var occurrenceNo=message.OccurrenceNo<=0?1:Math.Clamp(message.OccurrenceNo,1,5);
        var presentationId=string.IsNullOrWhiteSpace(message.PresentationId) && !string.IsNullOrWhiteSpace(groupKey)
            ? $"{groupKey}:{occurrenceNo}" : message.PresentationId;
        return message with
        {
            PayloadVersion = message.PayloadVersion <= 0 ? 1 : message.PayloadVersion,
            SourceType = string.IsNullOrWhiteSpace(message.SourceType) ? "order_submitted" : message.SourceType,
            SourceId = sourceId,
            Action = string.IsNullOrWhiteSpace(message.Action)
                ? (string.IsNullOrWhiteSpace(message.OrderId) ? "open_notifications" : "view_order")
                : message.Action,
            Priority = string.Equals(message.Priority,"high",StringComparison.OrdinalIgnoreCase)?"high":"normal",
            ExpiresAfterMinutes = message.ExpiresAfterMinutes<=0?15:Math.Clamp(message.ExpiresAfterMinutes,1,1440),
            OccurrenceNo=occurrenceNo,
            GroupKey=groupKey,
            PresentationId=presentationId,
        };
    }
    private sealed class SettingsRow { public string OwnerKey{get;set;}="";public long Revision{get;set;} public string RulesJson{get;set;}="[]"; }
    private sealed record NotificationSettingsDocument(int SchemaVersion,MenuNotificationRule[] Rules);
    private sealed class UserRow
    {
        public string Id{get;set;}=""; public string? StaffId{get;set;} public bool IsWorkingToday{get;set;} public string ActiveRolesJson{get;set;}="[]";
        public string[] ActiveRoles
        {
            get
            {
                try{return JsonSerializer.Deserialize<string[]>(ActiveRolesJson,MenuPolicies.Json)?.Select(x=>x.Trim().ToLowerInvariant()).Where(x=>x.Length>0).ToArray()??[];}
                catch(JsonException){return [];}
            }
        }
    }
    private sealed record PlannedDelivery(string AccountId,MenuNotificationMessage Content);
    private sealed class OutboxRow { public string Id{get;set;}="";public string SourceKey{get;set;}="";public string PayloadJson{get;set;}="[]";public DateTime CreatedAt{get;set;} public int Attempts{get;set;} }
    private sealed class OutboxFailureRow { public int Attempts{get;set;} }
    private sealed class DeliveryRow
    {
        public string Id{get;set;}="";public string PayloadJson{get;set;}="";public DateTime CreatedAt{get;set;}
        public DateTime ExpiresAt{get;set;}public DateTime? ReadAt{get;set;}
        public DateTime? AcknowledgedAt{get;set;}public DateTime? WithdrawnAt{get;set;}
    }
    private sealed class ChangeRow
    {
        public long Sequence{get;set;} public string ChangeId{get;set;}=""; public string? DeliveryId{get;set;}
        public string ChangeType{get;set;}=""; public DateTime CreatedAt{get;set;}
    }
    private sealed class AcknowledgeDeliveryRow
    {
        public string Id{get;set;}="";public DateTime ExpiresAt{get;set;}
        public DateTime? AcknowledgedAt{get;set;}public DateTime? WithdrawnAt{get;set;}
    }
    private sealed class DeliveryStateRow
    {
        public string Id{get;set;}="";public string RecipientAccountId{get;set;}="";public string PayloadJson{get;set;}="[]";
        public DateTime? AcknowledgedAt{get;set;}public DateTime? WithdrawnAt{get;set;}
    }
    private sealed class DeliveryMatchRow
    {
        public string DeliveryId{get;set;}="";public string MatchKey{get;set;}="";public string SourceKey{get;set;}="";
        public string SourceType{get;set;}="";public string? SourceId{get;set;}public string OwnerKey{get;set;}="";
        public string RuleId{get;set;}="";public string RuleType{get;set;}="";public long RuleRevision{get;set;}
        public string Fingerprint{get;set;}="";public string PopupMode{get;set;}="toast";public string? SoundId{get;set;}
        public string Priority{get;set;}="normal";public int ExpiresAfterMinutes{get;set;}=15;
        public bool IsBroadcast{get;set;}public bool RequiresAck{get;set;}
        public int RepeatIntervalMinutes{get;set;} public int MaxOccurrences{get;set;}=1;
        public DateTime? AcknowledgedAt{get;set;}public DateTime? WithdrawnAt{get;set;}
    }
    private sealed class RepeatInstanceRow
    {
        public string DeliveryId{get;set;}=""; public string MatchKey{get;set;}=""; public string SourceKey{get;set;}="";
        public string SourceType{get;set;}=""; public string? SourceId{get;set;} public string? EpisodeId{get;set;}
        public int IntervalMinutes{get;set;} public int OccurrenceNo{get;set;} public int MaxOccurrences{get;set;}
        public DateTime? NextAt{get;set;} public string Status{get;set;}="pending";
    }
    private sealed class RepeatDeliveryRow
    {
        public string Id{get;set;}=""; public string SourceKey{get;set;}=""; public string RecipientAccountId{get;set;}="";
        public string PayloadJson{get;set;}="[]"; public DateTime CreatedAt{get;set;} public DateTime ExpiresAt{get;set;}
        public int OccurrenceNo{get;set;} public string? EpisodeId{get;set;}
        public DateTime? AcknowledgedAt{get;set;} public DateTime? WithdrawnAt{get;set;}
    }
    private sealed class BacklogEpisodeRow
    {
        public string EpisodeId{get;set;}=""; public string RuleId{get;set;}=""; public long RuleRevision{get;set;}
        public string RuleFingerprint{get;set;}=""; public int Threshold{get;set;} public int RequiredDurationMinutes{get;set;}
        public int PendingCount{get;set;} public DateTime FirstSeenAt{get;set;} public DateTime LastSeenAt{get;set;}
        public DateTime? ActivatedAt{get;set;} public int OccurrenceNo{get;set;} public string Status{get;set;}="watching";
    }
    private sealed class ReceiptRow
    {
        public string Id{get;set;}="";public DateTime? ReadAt{get;set;}public DateTime ExpiresAt{get;set;}
        public bool HasAck{get;set;}public bool HasAcknowledged{get;set;}public bool HasPendingAck{get;set;}
        public bool IsWithdrawn{get;set;}
    }
    private sealed class BusinessScheduleSettingsRow
    {
        public int StartsAtMinute{get;set;} public int EndsAtMinute{get;set;} public bool EndsNextDay{get;set;}
        public string? UpdatedBy{get;set;}
    }
    private sealed class BusinessOverrideRow
    {
        public DateTime BusinessDate{get;set;} public DateTime StartsAt{get;set;} public DateTime EndsAt{get;set;}
        public DateTime ExpiresAt{get;set;}
    }
    private sealed class BusinessScheduleContext
    {
        public BusinessScheduleContext(string sourceId,DateTime businessDate,DateTime startsAt,DateTime scheduledEndsAt,
            DateTime? actualOpenedAt,DateTime? projectedCloseAt,DateTime? actualClosedAt,string periodStatus)
        {
            SourceId=sourceId; BusinessDate=businessDate.Date; StartsAt=startsAt;
            ScheduledEndsAt=scheduledEndsAt; ActualOpenedAt=actualOpenedAt; ProjectedCloseAt=projectedCloseAt;
            ActualClosedAt=actualClosedAt; PeriodStatus=periodStatus;
        }
        public string SourceId{get;} public DateTime BusinessDate{get;} public DateTime StartsAt{get;}
        public DateTime ScheduledEndsAt{get;} public DateTime? ActualOpenedAt{get;}
        public DateTime? ProjectedCloseAt{get;} public DateTime? ActualClosedAt{get;} public string PeriodStatus{get;}
    }
    private sealed record BusinessWindow(DateOnly BusinessDate,DateTime StartsAt,DateTime EndsAt);
    private sealed record BusinessSchedulePlan(string SourceId,string OwnerKey,string RuleId,string RuleType,
        long RuleRevision,string RuleFingerprint,int OffsetMinutes,long ScheduleRevision,DateTime DueAt,
        DateTime ExpiresAt,string ScheduleKey);
    private sealed class BusinessScheduleExistingRow
    {
        public string Id{get;set;}=""; public string ScheduleKey{get;set;}=""; public string SourceId{get;set;}="";
        public string OwnerKey{get;set;}=""; public string RuleId{get;set;}=""; public string RuleType{get;set;}="";
        public long RuleRevision{get;set;} public string RuleFingerprint{get;set;}=""; public int OffsetMinutes{get;set;}
        public long ScheduleRevision{get;set;} public DateTime DueAt{get;set;} public string Status{get;set;}="";
    }
    private sealed class ScheduleDeliveryRow
    {
        public string SourceId{get;set;}=""; public string OwnerKey{get;set;}=""; public string RuleId{get;set;}="";
        public string RuleType{get;set;}=""; public long RuleRevision{get;set;} public string RuleFingerprint{get;set;}="";
        public DateTime DueAt{get;set;} public DateTime ExpiresAt{get;set;} public string Status{get;set;}="";
    }
    private sealed class ScheduleRevisionRow { public string NomineeId{get;set;}=""; public long MaxRevision{get;set;} }
    private sealed class ScheduleRow
    {
        public string Id{get;set;}=""; public string ScheduleKey{get;set;}=""; public string SourceType{get;set;}=""; public string SourceId{get;set;}="";
        public string OrderId{get;set;}=""; public string NomineeId{get;set;}=""; public string OwnerKey{get;set;}="";
        public string RuleId{get;set;}=""; public string RuleType{get;set;}=""; public long RuleRevision{get;set;} public string RuleFingerprint{get;set;}="";
        public DateTime DueAt{get;set;}
    }
    private sealed class ScheduleSourceRow
    {
        public string OrderId{get;set;}=""; public string OrderNumber{get;set;}=""; public string OrderStatus{get;set;}="";
        public string StaffId{get;set;}=""; public string StaffName{get;set;}=""; public string ConfirmationStatus{get;set;}="";
    }
}

public sealed class MenuNotificationWorker(IServiceScopeFactory scopes,ILogger<MenuNotificationWorker> logger,IConfiguration config):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue<bool>("Notifications:Enabled")) return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope=scopes.CreateScope();
                var service=scope.ServiceProvider.GetRequiredService<MenuNotifications>();
                await service.ReconcileBusinessSchedulesAsync(stoppingToken);
                await service.ReconcileBacklogAsync(stoppingToken);
                for(var i=0;i<100;i++)
                {
                    var repeatProcessed=await service.DispatchRepeatOneAsync(stoppingToken);
                    var scheduleProcessed=await service.DispatchScheduleOneAsync(stoppingToken);
                    var outboxProcessed=await service.DispatchOneAsync(stoppingToken);
                    if(!repeatProcessed&&!scheduleProcessed&&!outboxProcessed) break;
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Menu notification dispatch failed; transaction will retry next cycle.");}
        }
    }
}
