using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Services.Menu;

public sealed record NotificationSoundDto(string Id,string Name,string? SystemCode,int DurationMs)
{
    public bool IsActive { get; init; } = true;
    public int Version { get; init; } = 1;
    public bool CanDelete { get; init; }
}
public sealed class NotificationSounds(AppDbContext db,IConfiguration config,IAppClock clock)
{
    private static readonly HashSet<string> BaseSystemCodes=["order_chime","time_reminder","store_broadcast"];
    private string Root => Path.Combine(Path.GetFullPath(config["Media:RootPath"]??"App_Data/media"),"notification-sounds");
    private static string? Staff(ClaimsPrincipal user)=>user.FindFirst(AdminAuthConstants.StaffMemberIdClaimType)?.Value;
    public async Task<IReadOnlyList<NotificationSoundDto>> ListAsync(ClaimsPrincipal user,CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        var rows=(await connection.QueryAsync<SoundRow>(new CommandDefinition("""
            SELECT ID AS Id,NAME AS Name,SYSTEM_CODE AS SystemCode,DURATION_MS AS DurationMs,
                   OWNER_STAFF_ID AS OwnerStaffId,IS_ACTIVE AS IsActive,VERSION AS Version
            FROM NOTIFICATION_SOUNDS
            WHERE IS_ACTIVE=TRUE AND (SYSTEM_CODE IS NOT NULL OR OWNER_STAFF_ID=@Staff)
            ORDER BY SYSTEM_CODE,NAME;
            """,new{Staff=Staff(user)},cancellationToken:ct))).ToArray();
        var role=user.FindFirst(AdminAuthConstants.RoleClaimType)?.Value;
        var staff=Staff(user);
        return rows.Select(row=>new NotificationSoundDto(row.Id,row.Name,row.SystemCode,row.DurationMs)
        {
            IsActive=row.IsActive,Version=row.Version,
            CanDelete=row.SystemCode is null?row.OwnerStaffId==staff:
                role=="developer"&&!BaseSystemCodes.Contains(row.SystemCode),
        }).ToArray();
    }
    public static async Task ValidateAccessAsync(MySqlConnection connection,MySqlTransaction? tx,string id,ClaimsPrincipal user,bool requireSystem,CancellationToken ct)
    {
        var allowed=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM NOTIFICATION_SOUNDS WHERE ID=@Id AND IS_ACTIVE=TRUE AND (SYSTEM_CODE IS NOT NULL OR (@Personal=TRUE AND OWNER_STAFF_ID=@Staff));",new{Id=id,Personal=!requireSystem,Staff=Staff(user)},tx,cancellationToken:ct));
        if(!allowed) throw new BusinessException("音效不存在或無權使用。","NOTIFICATION_SOUND_UNAVAILABLE");
    }
    public async Task<(string Path,string Mime)> ContentAsync(ClaimsPrincipal user,string id,CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await ValidateAccessAsync(connection,null,id,user,false,ct);
        var row=await connection.QuerySingleAsync<SoundFile>(new CommandDefinition("SELECT FILE_NAME AS FileName,MIME_TYPE AS Mime FROM NOTIFICATION_SOUNDS WHERE ID=@Id;",new{Id=id},cancellationToken:ct));
        var path=Path.Combine(Root,Path.GetFileName(row.FileName));
        if(!File.Exists(path)) throw new NotFoundException("音效檔案不可用。","NOTIFICATION_SOUND_MISSING");
        return(path,row.Mime);
    }
    public async Task<NotificationSoundDto> UploadAsync(ClaimsPrincipal user,IFormFile file,string name,string? systemCode,CancellationToken ct)
    {
        var staff=Staff(user);var role=user.FindFirst(AdminAuthConstants.RoleClaimType)?.Value;var system=!string.IsNullOrWhiteSpace(systemCode);
        if(system&&role!="developer") throw new ForbiddenException("僅開發者可新增系統音效。");
        if(!system&&string.IsNullOrWhiteSpace(staff)) throw new BusinessException("需要關聯店員身分。","NOTIFICATION_STAFF_REQUIRED");
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>80 || (system&&!System.Text.RegularExpressions.Regex.IsMatch(systemCode!,"^[a-z][a-z0-9_]{0,59}$"))) throw new BusinessException("音效名稱或系統代碼無效。","NOTIFICATION_SOUND_INVALID");
        var extension=Path.GetExtension(file.FileName).ToLowerInvariant();
        if(file.Length is <=0 or >1048576 || extension is not (".ogg" or ".mp3")) throw new BusinessException("請上傳最多 1 MiB 的 Ogg/Opus 或 MP3。","NOTIFICATION_SOUND_INVALID");
        var probe=config["Notifications:FFprobePath"];var decoder=config["Notifications:FFmpegPath"];
        if(string.IsNullOrWhiteSpace(probe)||string.IsNullOrWhiteSpace(decoder)) throw new BusinessException("音效解析器尚未配置，請由開發者設定 FFprobePath 與 FFmpegPath。","NOTIFICATION_DECODER_REQUIRED");
        Directory.CreateDirectory(Root);
        var id=Guid.NewGuid().ToString();var temporary=Path.Combine(Root,id+".upload");var final=Path.Combine(Root,id+extension);
        try
        {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {await file.CopyToAsync(output,ct);if(output.Length>1048576)throw new BusinessException("音效超過大小限制。","NOTIFICATION_SOUND_INVALID");}
            var json=await RunAsync(probe,["-v","error","-show_entries","format=duration:stream=codec_name,codec_type","-of","json",temporary],ct);
            using var document=JsonDocument.Parse(json);
            var streams=document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var duration=double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
            if(streams.Length!=1||streams[0].GetProperty("codec_type").GetString()!="audio"||streams[0].GetProperty("codec_name").GetString()!=(extension==".ogg"?"opus":"mp3")||!double.IsFinite(duration)||duration<=0||duration>5)
                throw new BusinessException("音效必須可解碼且不超過 5 秒（Ogg/Opus 或 MP3）。","NOTIFICATION_SOUND_INVALID");
            await RunAsync(decoder,["-v","error","-xerror","-threads","1","-i",temporary,"-t","6","-f","null","-"],ct);
            File.Move(temporary,final);
            await using var hashStream=File.OpenRead(final);
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(hashStream,ct));
            await using var connection=await db.CreateOpenConnectionAsync(ct);
            await using var tx=await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_SOUNDS (ID,OWNER_STAFF_ID,SYSTEM_CODE,NAME,FILE_NAME,MIME_TYPE,DURATION_MS,IS_ACTIVE,VERSION,SHA256_HASH,CREATED_AT,UPDATED_AT) VALUES (@Id,@Staff,@Code,@Name,@File,@Mime,@Duration,TRUE,1,@Hash,@Now,@Now);",new{Id=id,Staff=system?null:staff,Code=system?systemCode:null,Name=name.Trim(),File=id+extension,Mime=extension==".ogg"?"audio/ogg":"audio/mpeg",Duration=(int)Math.Ceiling(duration*1000),Hash=hash,Now=clock.LocalDateTime},tx,cancellationToken:ct));
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,CREATED_AT) VALUES (@Id,@Actor,'sound.uploaded',@Sound,@Now);",new{Id=Guid.NewGuid().ToString(),Actor=MenuNotifications.Account(user),Sound=id,Now=clock.LocalDateTime},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return new(id,name.Trim(),system?systemCode:null,(int)Math.Ceiling(duration*1000))
            {
                IsActive=true,Version=1,CanDelete=!system||(role=="developer"&&!BaseSystemCodes.Contains(systemCode!)),
            };
        }
        catch(MySqlException ex) when(ex.Number==1062)
        {if(File.Exists(final))File.Delete(final);throw new ConflictException("系統音效代碼已存在，請使用其他代碼。","NOTIFICATION_SOUND_DUPLICATED");}
        catch {if(File.Exists(final))File.Delete(final);throw;}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    public async Task<NotificationSoundDto> DeleteAsync(ClaimsPrincipal user,string id,CancellationToken ct)
    {
        var account=MenuNotifications.Account(user);var staff=Staff(user);
        var role=user.FindFirst(AdminAuthConstants.RoleClaimType)?.Value;
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        var row=await connection.QuerySingleOrDefaultAsync<SoundRow>(new CommandDefinition("""
            SELECT ID AS Id,NAME AS Name,SYSTEM_CODE AS SystemCode,DURATION_MS AS DurationMs,
                   OWNER_STAFF_ID AS OwnerStaffId,IS_ACTIVE AS IsActive,VERSION AS Version
            FROM `NOTIFICATION_SOUNDS` WHERE ID=@Id FOR UPDATE;
            """,new{Id=id},tx,cancellationToken:ct));
        if(row is null) throw new NotFoundException("找不到音效。","NOTIFICATION_SOUND_NOT_FOUND");
        if(row.SystemCode is null)
        {
            if(row.OwnerStaffId!=staff) throw new ForbiddenException("只能刪除自己的音效。");
        }
        else
        {
            if(BaseSystemCodes.Contains(row.SystemCode)) throw new BusinessException("三個基礎系統音效不可刪除。","NOTIFICATION_SOUND_PROTECTED");
            if(role!="developer") throw new ForbiddenException("僅開發者可管理系統音效。");
        }
        if(!row.IsActive)
        {
            await tx.CommitAsync(ct);
            return ToDto(row,false);
        }
        var settings=(await connection.QueryAsync<string>(new CommandDefinition("SELECT RULES_JSON FROM NOTIFICATION_SETTINGS;",transaction:tx,cancellationToken:ct))).ToArray();
        var pending=(await connection.QueryAsync<string>(new CommandDefinition("SELECT PAYLOAD_JSON FROM NOTIFICATION_OUTBOX WHERE PROCESSED_AT IS NULL AND IS_QUARANTINED=FALSE;",transaction:tx,cancellationToken:ct))).ToArray();
        var deliveries=(await connection.QueryAsync<string>(new CommandDefinition("SELECT PAYLOAD_JSON FROM NOTIFICATION_DELIVERIES WHERE EXPIRES_AT>@Now AND WITHDRAWN_AT IS NULL;",new{Now=clock.LocalDateTime},tx,cancellationToken:ct))).ToArray();
        var matchReferences=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM NOTIFICATION_DELIVERY_MATCHES M JOIN NOTIFICATION_DELIVERIES D ON D.ID=M.DELIVERY_ID WHERE M.SOUND_ID=@Id AND M.WITHDRAWN_AT IS NULL AND D.EXPIRES_AT>@Now AND D.WITHDRAWN_AT IS NULL;",new{Id=id,Now=clock.LocalDateTime},tx,cancellationToken:ct));
        var marker=$"\"soundId\":\"{id}\"";
        if(settings.Any(x=>x.Contains(marker,StringComparison.Ordinal))||pending.Any(x=>x.Contains(marker,StringComparison.Ordinal))||deliveries.Any(x=>x.Contains(marker,StringComparison.Ordinal))||matchReferences>0)
            throw new BusinessException("音效仍被規則、待發通知或有效通知引用，請先更換音效或等待通知失效。","NOTIFICATION_SOUND_IN_USE");
        var now=clock.LocalDateTime;
        await connection.ExecuteAsync(new CommandDefinition("UPDATE `NOTIFICATION_SOUNDS` SET `IS_ACTIVE`=FALSE,`VERSION`=`VERSION`+1,`UPDATED_AT`=@Now,`DELETED_AT`=@Now WHERE ID=@Id AND IS_ACTIVE=TRUE;",new{Id=id,Now=now},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,BEFORE_JSON,AFTER_JSON,CREATED_AT) VALUES (@AuditId,@Actor,'sound.deleted',@Sound,@Before,@After,@Now);",new
        {
            AuditId=Guid.NewGuid().ToString(),Actor=account,Sound=id,
            Before=JsonSerializer.Serialize(row,MenuPolicies.Json),After=JsonSerializer.Serialize(new{isActive=false,deletedAt=now},MenuPolicies.Json),Now=now,
        },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new NotificationSoundDto(row.Id,row.Name,row.SystemCode,row.DurationMs){IsActive=false,Version=row.Version+1,CanDelete=false};
    }
    private static NotificationSoundDto ToDto(SoundRow row,bool canDelete)
        => new(row.Id,row.Name,row.SystemCode,row.DurationMs){IsActive=row.IsActive,Version=row.Version,CanDelete=canDelete};
    private static async Task<string> RunAsync(string executable,string[] arguments,CancellationToken ct)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(var argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            process.Start();var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);await error;
            if(process.ExitCode!=0)throw new BusinessException("無法解析或解碼音效。","NOTIFICATION_SOUND_INVALID");
            return await output;
        }
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw;}
    }
    private sealed class SoundFile{public string FileName{get;set;}="";public string Mime{get;set;}="";}
    private sealed class SoundRow
    {
        public string Id{get;set;}="";public string Name{get;set;}="";public string? SystemCode{get;set;}
        public int DurationMs{get;set;} public string? OwnerStaffId{get;set;} public bool IsActive{get;set;}=true;public int Version{get;set;}=1;
    }
}
