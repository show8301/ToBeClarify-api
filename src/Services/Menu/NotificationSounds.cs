using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;

namespace ToBeClarify.Api.Services.Menu;

public sealed record NotificationSoundDto(string Id,string Name,string? SystemCode,int DurationMs);
public sealed class NotificationSounds(AppDbContext db,IConfiguration config,IAppClock clock)
{
    private string Root => Path.Combine(Path.GetFullPath(config["Media:RootPath"]??"App_Data/media"),"notification-sounds");
    private static string? Staff(ClaimsPrincipal user)=>user.FindFirst(AdminAuthConstants.StaffMemberIdClaimType)?.Value;
    public async Task<IReadOnlyList<NotificationSoundDto>> ListAsync(ClaimsPrincipal user,CancellationToken ct)
    {
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        return (await connection.QueryAsync<NotificationSoundDto>(new CommandDefinition("SELECT ID AS Id,NAME AS Name,SYSTEM_CODE AS SystemCode,DURATION_MS AS DurationMs FROM NOTIFICATION_SOUNDS WHERE SYSTEM_CODE IS NOT NULL OR OWNER_STAFF_ID=@Staff ORDER BY SYSTEM_CODE,NAME;",new{Staff=Staff(user)},cancellationToken:ct))).ToArray();
    }
    public static async Task ValidateAccessAsync(MySqlConnection connection,MySqlTransaction? tx,string id,ClaimsPrincipal user,bool requireSystem,CancellationToken ct)
    {
        var allowed=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COUNT(*)>0 FROM NOTIFICATION_SOUNDS WHERE ID=@Id AND (SYSTEM_CODE IS NOT NULL OR (@Personal=TRUE AND OWNER_STAFF_ID=@Staff));",new{Id=id,Personal=!requireSystem,Staff=Staff(user)},tx,cancellationToken:ct));
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
        var staff=Staff(user);var system=!string.IsNullOrWhiteSpace(systemCode);
        if(system&&user.FindFirst(AdminAuthConstants.RoleClaimType)?.Value!="developer") throw new ForbiddenException("僅開發者可新增系統音效。");
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
            await using var connection=await db.CreateOpenConnectionAsync(ct);
            await using var tx=await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_SOUNDS (ID,OWNER_STAFF_ID,SYSTEM_CODE,NAME,FILE_NAME,MIME_TYPE,DURATION_MS,CREATED_AT) VALUES (@Id,@Staff,@Code,@Name,@File,@Mime,@Duration,@Now);",new{Id=id,Staff=system?null:staff,Code=system?systemCode:null,Name=name.Trim(),File=id+extension,Mime=extension==".ogg"?"audio/ogg":"audio/mpeg",Duration=(int)Math.Ceiling(duration*1000),Now=clock.LocalDateTime},tx,cancellationToken:ct));
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO NOTIFICATION_AUDIT (ID,ACTOR_ACCOUNT_ID,ACTION_TYPE,ENTITY_ID,CREATED_AT) VALUES (@Id,@Actor,'sound.uploaded',@Sound,@Now);",new{Id=Guid.NewGuid().ToString(),Actor=MenuNotifications.Account(user),Sound=id,Now=clock.LocalDateTime},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return new(id,name.Trim(),system?systemCode:null,(int)Math.Ceiling(duration*1000));
        }
        catch {if(File.Exists(final))File.Delete(final);throw;}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
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
}
