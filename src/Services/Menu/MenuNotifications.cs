using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
    public string RuleType { get; init; } = "order_received";
    public bool IsEnabled { get; init; } = true;
    public string PopupMode { get; init; } = "toast";
    public string? SoundId { get; init; }
}
public sealed record MenuNotificationSettings(long Revision, IReadOnlyList<MenuNotificationRule> Rules);
public sealed class SaveMenuNotificationsRequest
{
    [Range(0,long.MaxValue)] public long ExpectedRevision { get; init; }
    public MenuNotificationRule[] Rules { get; init; } = [];
}
public sealed record MenuNotificationMessage(string OrderId, string Title, string Message,
    string PopupMode, string? SoundId, bool IsBroadcast, string[] MatchedRules);
public sealed record MenuNotificationDelivery(string Id, MenuNotificationMessage Content,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ReadAt);
public sealed record MenuNotificationInbox(IReadOnlyList<MenuNotificationDelivery> Items, int UnreadCount);

public sealed class MenuNotifications(AppDbContext db, IAppClock clock)
{
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
        return new(row?.Revision??0, row is null?[]:MenuPolicies.Read<MenuNotificationRule[]>(row.RulesJson));
    }
    public async Task<MenuNotificationSettings> SaveAsync(ClaimsPrincipal user, bool broadcast, SaveMenuNotificationsRequest request, CancellationToken ct)
    {
        var owner = Owner(user,broadcast); var actor = Account(user);
        if (request.Rules is null || request.Rules.Length>20 || request.Rules.Select(x=>x.Id).Distinct().Count()!=request.Rules.Length || request.Rules.Select(x=>x.RuleType).Distinct().Count()!=request.Rules.Length)
            throw new BusinessException("相同觸發條件只能設定一次。", "NOTIFICATION_RULE_DUPLICATED");
        foreach(var rule in request.Rules)
        {
            if(!Guid.TryParse(rule.Id,out _) || rule.RuleType is not ("order_received" or "champagne_order_received") ||
                (broadcast ? rule.PopupMode!="banner" : rule.PopupMode is not ("none" or "toast" or "sticky")))
                throw new BusinessException("通知規則參數無效。", "NOTIFICATION_RULE_INVALID");
            if(broadcast && rule.IsEnabled && rule.SoundId is null)
                throw new BusinessException("店內廣播啟用前必須選擇有效系統音效。", "BROADCAST_SOUND_REQUIRED");
        }
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO NOTIFICATION_SETTINGS (OWNER_KEY,REVISION,RULES_JSON,UPDATED_AT,UPDATED_BY) VALUES (@Owner,0,'[]',@Now,@Actor);",new {Owner=owner,Now=clock.LocalDateTime,Actor=actor},tx,cancellationToken:ct));
        var row=await connection.QuerySingleAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS WHERE OWNER_KEY=@Owner FOR UPDATE;",new {Owner=owner},tx,cancellationToken:ct));
        if(row.Revision!=request.ExpectedRevision) throw new ConflictException("通知設定已變更，請重新載入比對草稿。","NOTIFICATION_REVISION_CONFLICT");
        foreach(var rule in request.Rules.Where(x=>x.SoundId is not null))
            await NotificationSounds.ValidateAccessAsync(connection,tx,rule.SoundId!,user,broadcast,ct);
        var json=JsonSerializer.Serialize(request.Rules,MenuPolicies.Json);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_SETTINGS SET REVISION=REVISION+1,RULES_JSON=@Json,UPDATED_AT=@Now,UPDATED_BY=@Actor WHERE OWNER_KEY=@Owner;",new {Owner=owner,Json=json,Now=clock.LocalDateTime,Actor=actor},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@Id,@Actor,'settings.updated',@Owner,@Before,@After,@Now);",new {Id=Guid.NewGuid().ToString(),Actor=actor,Owner=owner,Before=row.RulesJson,After=json,Now=clock.LocalDateTime},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(row.Revision+1,request.Rules);
    }

    public static async Task EnqueueAsync(MySqlConnection connection,MySqlTransaction tx,NewOrderAggregate order,CancellationToken ct)
    {
        if(!order.Items.Any(x=>x.ItemType is "menu_item" or "menu_set")) return;
        var snapshot=JsonSerializer.Deserialize<MenuOrderSnapshot>(order.MenuSnapshotJson!,MenuPolicies.Json)!;
        var champagne=snapshot.Lines.Any(x=>x.EventCategories.Contains("champagne_tower"));
        var settings=(await connection.QueryAsync<SettingsRow>(new CommandDefinition("SELECT OWNER_KEY AS OwnerKey,REVISION AS Revision,RULES_JSON AS RulesJson FROM NOTIFICATION_SETTINGS ORDER BY OWNER_KEY;",transaction:tx,cancellationToken:ct))).ToDictionary(x=>x.OwnerKey);
        var users=await connection.QueryAsync<UserRow>(new CommandDefinition("SELECT ID AS Id,STAFF_MEMBER_ID AS StaffId FROM ADMIN_USERS WHERE IS_ACTIVE=TRUE;",transaction:tx,cancellationToken:ct));
        var deliveries=new List<PlannedDelivery>();
        foreach(var user in users)
        {
            var matches=new List<(MenuNotificationRule Rule,bool Broadcast,long Revision)>();
            foreach(var key in new[]{"broadcast","staff:"+user.StaffId})
                if(settings.TryGetValue(key,out var entry))
                    matches.AddRange(MenuPolicies.Read<MenuNotificationRule[]>(entry.RulesJson)
                        .Where(x=>x.IsEnabled&&(x.RuleType=="order_received" || (champagne&&x.RuleType=="champagne_order_received")))
                        .Select(x=>(x,key=="broadcast",entry.Revision)));
            if(matches.Count==0) continue;
            var isBroadcast=matches.Any(x=>x.Broadcast);
            var chosen=isBroadcast?matches.Where(x=>x.Broadcast).OrderBy(x=>x.Rule.Id,StringComparer.Ordinal).First().Rule:matches.FirstOrDefault(x=>x.Rule.SoundId is not null).Rule;
            var mode=isBroadcast?"banner":matches.Any(x=>x.Rule.PopupMode=="sticky")?"sticky":matches.Any(x=>x.Rule.PopupMode=="toast")?"toast":"none";
            var text=order.StoreConfirmationStatus=="pending"?"待店內確認":order.Status=="confirmed"?"已成立":"已提交，等待指名確認";
            deliveries.Add(new(user.Id,new(order.Id,champagne?"收到香檳塔點餐訂單":"收到點餐訂單",$"訂單 {order.OrderNumber} · {text}",mode,chosen?.SoundId,isBroadcast,
                matches.Select(x=>$"{(x.Broadcast?"broadcast":"personal")}:{x.Rule.Id}:{x.Revision}").ToArray())));
        }
        // Even an empty audience is snapshotted. Later settings must not replay this order.
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_OUTBOX (ID,SOURCE_KEY,PAYLOAD_JSON,CREATED_AT) VALUES (@Id,@Source,@Payload,@Now);",
            new{Id=Guid.NewGuid().ToString(),Source="order:"+order.Id+":submitted",Payload=JsonSerializer.Serialize(deliveries,MenuPolicies.Json),Now=order.SubmittedAt},tx,cancellationToken:ct));
    }

    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var row=await connection.QuerySingleOrDefaultAsync<OutboxRow>(new CommandDefinition("SELECT ID AS Id,SOURCE_KEY AS SourceKey,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt FROM NOTIFICATION_OUTBOX WHERE PROCESSED_AT IS NULL ORDER BY CREATED_AT,ID LIMIT 1 FOR UPDATE SKIP LOCKED;",transaction:tx,cancellationToken:ct));
        if(row is null) return false;
        var deliveries=JsonSerializer.Deserialize<PlannedDelivery[]>(row.PayloadJson,MenuPolicies.Json)!;
        foreach(var delivery in deliveries)
        {
            // Canceled orders remain history, but are never presented as actionable new work.
            var active=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM ORDERS WHERE ID=@Id AND ORDER_STATUS NOT IN ('cancelled','canceled','expired');",new{Id=delivery.Content.OrderId},tx,cancellationToken:ct));
            await connection.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO NOTIFICATION_DELIVERIES (ID,SOURCE_KEY,RECIPIENT_ACCOUNT_ID,PAYLOAD_JSON,CREATED_AT,EXPIRES_AT) VALUES (@Id,@Source,@Account,@Payload,@Created,@Expires);",
                new{Id=Guid.NewGuid().ToString(),Source=row.SourceKey,Account=delivery.AccountId,Payload=JsonSerializer.Serialize(delivery.Content,MenuPolicies.Json),Created=row.CreatedAt,Expires=active?row.CreatedAt.AddMinutes(15):clock.LocalDateTime},tx,cancellationToken:ct));
        }
        await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_OUTBOX SET PROCESSED_AT=@Now WHERE ID=@Id;",new{Now=clock.LocalDateTime,row.Id},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return true;
    }
    public async Task<MenuNotificationInbox> InboxAsync(ClaimsPrincipal user,CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        var args=new{Account=Account(user),Since=clock.LocalDateTime.AddDays(-30),Now=clock.LocalDateTime};
        var rows=await connection.QueryAsync<DeliveryRow>(new CommandDefinition("SELECT ID AS Id,PAYLOAD_JSON AS PayloadJson,CREATED_AT AS CreatedAt,EXPIRES_AT AS ExpiresAt,READ_AT AS ReadAt FROM NOTIFICATION_DELIVERIES WHERE RECIPIENT_ACCOUNT_ID=@Account AND CREATED_AT>=@Since ORDER BY CREATED_AT DESC,ID DESC LIMIT 100;",args,cancellationToken:ct));
        var count=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM NOTIFICATION_DELIVERIES WHERE RECIPIENT_ACCOUNT_ID=@Account AND CREATED_AT>=@Since AND EXPIRES_AT>@Now AND READ_AT IS NULL;",args,cancellationToken:ct));
        DateTimeOffset Time(DateTime x)=>new(x,TimeSpan.FromHours(8));
        return new(rows.Select(x=>new MenuNotificationDelivery(x.Id,JsonSerializer.Deserialize<MenuNotificationMessage>(x.PayloadJson,MenuPolicies.Json)!,Time(x.CreatedAt),Time(x.ExpiresAt),x.ReadAt.HasValue?Time(x.ReadAt.Value):null)).ToArray(),count);
    }
    public async Task ReadAsync(ClaimsPrincipal user,string[] ids,CancellationToken ct)
    {
        if(ids is null || ids.Length>100) throw new BusinessException("一次最多標記 100 筆。","NOTIFICATION_READ_INVALID");
        if(ids.Length==0) return;
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE NOTIFICATION_DELIVERIES SET READ_AT=COALESCE(READ_AT,@Now) WHERE RECIPIENT_ACCOUNT_ID=@Account AND ID IN @Ids;",new{Now=clock.LocalDateTime,Account=Account(user),Ids=ids},cancellationToken:ct));
    }
    private sealed class SettingsRow { public string OwnerKey{get;set;}="";public long Revision{get;set;} public string RulesJson{get;set;}="[]"; }
    private sealed class UserRow { public string Id{get;set;}=""; public string? StaffId{get;set;} }
    private sealed record PlannedDelivery(string AccountId,MenuNotificationMessage Content);
    private sealed class OutboxRow { public string Id{get;set;}="";public string SourceKey{get;set;}="";public string PayloadJson{get;set;}="[]";public DateTime CreatedAt{get;set;} }
    private sealed class DeliveryRow {public string Id{get;set;}="";public string PayloadJson{get;set;}="";public DateTime CreatedAt{get;set;}public DateTime ExpiresAt{get;set;}public DateTime? ReadAt{get;set;} }
}

public sealed class MenuNotificationWorker(IServiceScopeFactory scopes,ILogger<MenuNotificationWorker> logger,IConfiguration config):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue<bool>("Notifications:Enabled")) return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope=scopes.CreateScope(); var service=scope.ServiceProvider.GetRequiredService<MenuNotifications>(); for(var i=0;i<100&&await service.DispatchOneAsync(stoppingToken);i++){} }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Menu notification dispatch failed; transaction will retry next cycle.");}
        }
    }
}
