using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Services.Menu;

namespace ToBeClarify.Api.Controllers.Admin;

[ApiController,Authorize(Policy="AdminOnly"),Route("api/admin/notifications")]
public sealed class MenuNotificationsController(MenuNotifications notifications,NotificationSounds sounds,IConfiguration config):ControllerBase
{
    [HttpGet("capabilities")]
    public object Capabilities()=>ApiResponse<object>.Ok(new{enabled=config.GetValue<bool>("Notifications:Enabled"),ruleTypes=new[]{"order_received","champagne_order_received"},delivery="sse",soundUploadConfigured=!string.IsNullOrWhiteSpace(config["Notifications:FFprobePath"])&&!string.IsNullOrWhiteSpace(config["Notifications:FFmpegPath"])});
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        if(!config.GetValue<bool>("Notifications:Enabled")){Response.StatusCode=503;return;}
        Response.ContentType="text/event-stream";
        Response.Headers.CacheControl="private, no-store";
        Response.Headers["X-Accel-Buffering"]="no";
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        // Bounded connections re-authenticate on reconnect; the inbox also rechecks active accounts.
        for(var i=0;i<10&&!ct.IsCancellationRequested;i++)
        {
            var inbox=await notifications.InboxAsync(User,ct);
            await Response.WriteAsync("event: inbox\ndata: "+System.Text.Json.JsonSerializer.Serialize(inbox,MenuPolicies.Json)+"\n\n",ct);
            await Response.Body.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5),ct);
        }
    }
    [HttpGet]
    public async Task<object> Inbox(CancellationToken ct)=>ApiResponse<MenuNotificationInbox>.Ok(await notifications.InboxAsync(User,ct));
    [HttpGet("settings")]
    public async Task<object> Settings([FromQuery]bool broadcast,CancellationToken ct)=>ApiResponse<MenuNotificationSettings>.Ok(await notifications.SettingsAsync(User,broadcast,ct));
    [HttpPut("settings")]
    public async Task<object> Save(SaveMenuNotificationsRequest request,[FromQuery]bool broadcast,CancellationToken ct)=>ApiResponse<MenuNotificationSettings>.Ok(await notifications.SaveAsync(User,broadcast,request,ct));
    [HttpPost("read")]
    public async Task<object> Read(ReadNotificationRequest request,CancellationToken ct){await notifications.ReadAsync(User,request.Ids,ct);return ApiResponse<bool>.Ok(true);}
    [HttpGet("sounds")]
    public async Task<object> Sounds(CancellationToken ct)=>ApiResponse<IReadOnlyList<NotificationSoundDto>>.Ok(await sounds.ListAsync(User,ct));
    [HttpPost("sounds"),RequestSizeLimit(1100000),EnableRateLimiting("ordering-write")]
    public async Task<object> Upload([FromForm]IFormFile file,[FromForm]string name,[FromForm]string? systemCode,CancellationToken ct)=>ApiResponse<NotificationSoundDto>.Ok(await sounds.UploadAsync(User,file,name,systemCode,ct));
    [HttpGet("sounds/{id}/content")]
    public async Task<IActionResult> Content(string id,CancellationToken ct)
    {var result=await sounds.ContentAsync(User,id,ct);Response.Headers.CacheControl="private, no-store";return PhysicalFile(result.Path,result.Mime,enableRangeProcessing:true);}
    public sealed class ReadNotificationRequest{public string[] Ids{get;init;}=[];}
}
