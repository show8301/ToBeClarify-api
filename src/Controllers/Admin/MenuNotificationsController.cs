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
    public object Capabilities()=>ApiResponse<object>.Ok(new
    {
        schemaVersion=2,
        enabled=config.GetValue<bool>("Notifications:Enabled"),
        // Keep ruleTypes as the legacy/broadcast-compatible list. Consumers that
        // understand v2 should use the scope-specific lists below.
        ruleTypes=new[]{"order_received","champagne_order_received"},
        personalRuleTypes=new[]{"order_received","designated_order_received","nomination_starting","nomination_ending","nomination_ended","business_opening_soon","business_closing_soon","champagne_order_received"},
        broadcastRuleTypes=new[]{"order_received","champagne_order_received","order_backlog"},
        delivery="sse-v2",
        supportsSourcePayload=true,
        supportsCursor=true,
        supportsAcknowledgement=true,
        soundUploadConfigured=true
    });
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        if(!config.GetValue<bool>("Notifications:Enabled")){Response.StatusCode=503;return;}
        Response.ContentType="text/event-stream";
        Response.Headers.CacheControl="private, no-store";
        Response.Headers["X-Accel-Buffering"]="no";
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        var cursor=Request.Headers["Last-Event-ID"].FirstOrDefault() ?? Request.Query["cursor"].FirstOrDefault();
        if(string.IsNullOrWhiteSpace(cursor))
        {
            var initial=await notifications.InboxAsync(User,ct);
            cursor=initial.SnapshotCursor;
            await WriteEventAsync("inbox",cursor,initial,ct);
            await Response.Body.FlushAsync(ct);
        }
        // Bounded connections re-authenticate on reconnect; the inbox also rechecks active accounts.
        for(var i=0;i<12&&!ct.IsCancellationRequested;i++)
        {
            var changes=await notifications.ChangesAsync(User,cursor,100,ct);
            if(changes.ResyncRequired)
            {
                var snapshot=await notifications.InboxAsync(User,ct);
                cursor=snapshot.SnapshotCursor;
                await WriteEventAsync("resync",cursor,new{reason=changes.ResyncReason,cursor},ct);
                await WriteEventAsync("inbox",cursor,snapshot,ct);
            }
            else if(changes.Items.Count>0)
            {
                foreach(var change in changes.Items)
                    await WriteEventAsync("change",MenuNotifications.CursorForSequence(change.Sequence),change,ct);
                cursor=changes.Cursor;
                // Keep the legacy inbox event for clients that do not understand v2 changes.
                var inbox=await notifications.InboxAsync(User,ct);
                await WriteEventAsync("inbox",inbox.SnapshotCursor,inbox,ct);
            }
            else
                await Response.WriteAsync(": notification-heartbeat\n\n",ct);
            await Response.Body.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5),ct);
        }
    }
    private async Task WriteEventAsync(string eventName,string id,object data,CancellationToken ct)
        => await Response.WriteAsync($"id: {id}\nevent: {eventName}\ndata: {System.Text.Json.JsonSerializer.Serialize(data,MenuPolicies.Json)}\n\n",ct);
    [HttpGet]
    public async Task<object> Inbox([FromQuery]string? pageToken,[FromQuery]int limit=100,CancellationToken ct=default)
        =>ApiResponse<MenuNotificationInbox>.Ok(await notifications.InboxAsync(User,ct,pageToken,limit));
    [HttpGet("changes")]
    public async Task<object> Changes([FromQuery]string? cursor,[FromQuery]int limit=100,CancellationToken ct=default)
        =>ApiResponse<MenuNotificationChangePage>.Ok(await notifications.ChangesAsync(User,cursor,limit,ct));
    [HttpGet("settings")]
    public async Task<object> Settings([FromQuery]bool broadcast,CancellationToken ct)=>ApiResponse<MenuNotificationSettings>.Ok(await notifications.SettingsAsync(User,broadcast,ct));
    [HttpPut("settings")]
    public async Task<object> Save(SaveMenuNotificationsRequest request,[FromQuery]bool broadcast,CancellationToken ct)=>ApiResponse<MenuNotificationSettings>.Ok(await notifications.SaveAsync(User,broadcast,request,ct));
    [HttpPost("broadcasts/send"),EnableRateLimiting("ordering-write")]
    public async Task<object> SendBroadcast(SendBroadcastRequest request,CancellationToken ct)
        =>ApiResponse<BroadcastSendResult>.Ok(await notifications.SendBroadcastAsync(User,request,ct));
    [HttpPost("read")]
    public async Task<object> Read(ReadNotificationRequest request,CancellationToken ct){await notifications.ReadAsync(User,request.Ids,ct);return ApiResponse<bool>.Ok(true);}
    [HttpPost("{id}/acknowledge"),EnableRateLimiting("ordering-write")]
    public async Task<object> Acknowledge(string id,CancellationToken ct)
        =>ApiResponse<NotificationAcknowledgementResult>.Ok(await notifications.AcknowledgeAsync(User,id,ct));
    [HttpPost("broadcasts/{id}/withdraw"),EnableRateLimiting("ordering-write")]
    public async Task<object> WithdrawBroadcast(string id,CancellationToken ct)
        =>ApiResponse<BroadcastWithdrawResult>.Ok(await notifications.WithdrawBroadcastAsync(User,id,ct));
    [HttpGet("broadcasts/{id}/receipts")]
    public async Task<object> BroadcastReceipts(string id,CancellationToken ct)
        =>ApiResponse<BroadcastReceiptSummary>.Ok(await notifications.BroadcastReceiptsAsync(User,id,ct));
    [HttpGet("sounds")]
    public async Task<object> Sounds(CancellationToken ct)=>ApiResponse<IReadOnlyList<NotificationSoundDto>>.Ok(await sounds.ListAsync(User,ct));
    [HttpPost("sounds"),RequestSizeLimit(1100000),EnableRateLimiting("ordering-write")]
    public async Task<object> Upload([FromForm]IFormFile file,[FromForm]string name,[FromForm]string? systemCode,CancellationToken ct)=>ApiResponse<NotificationSoundDto>.Ok(await sounds.UploadAsync(User,file,name,systemCode,ct));
    [HttpDelete("sounds/{id}"),EnableRateLimiting("ordering-write")]
    public async Task<object> DeleteSound(string id,CancellationToken ct)=>ApiResponse<NotificationSoundDto>.Ok(await sounds.DeleteAsync(User,id,ct));
    [HttpGet("sounds/{id}/content")]
    public async Task<IActionResult> Content(string id,CancellationToken ct)
    {var result=await sounds.ContentAsync(User,id,ct);Response.Headers.CacheControl="private, no-store";return PhysicalFile(result.Path,result.Mime,enableRangeProcessing:true);}
    public sealed class ReadNotificationRequest{public string[] Ids{get;init;}=[];}
}
