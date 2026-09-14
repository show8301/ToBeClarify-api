using System.Security.Claims;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Shared;
using ToBeClarify.Api.Services.Client.Guestbook;

namespace ToBeClarify.Api.Services.Admin.Guestbook;

public sealed class AdminGuestbookService(GuestbookStore store)
{
    private static string Actor(ClaimsPrincipal actor) => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType) ?? throw new UnauthorizedException("登入已失效。");
    public Task<GuestbookList> List(int page, int size, string filter, CancellationToken ct)
    {
        GuestbookBoardService.Page(page, size);
        if (filter is not ("all" or "hidden" or "locked")) throw new BusinessException("篩選條件不正確。");
        return store.List(page, size, true, filter, ct);
    }
    public Task<GuestbookReplies> Replies(string id, int page, int size, CancellationToken ct, string? cursor = null)
    {
        GuestbookBoardService.Page(page, size);
        return store.Replies(id, page, size, true, ct, cursor);
    }
    public async Task<GuestbookMessage> Create(string? thread, GuestbookWrite write, ClaimsPrincipal actor, CancellationToken ct)
    {
        var id = Actor(actor);
        var staffId = actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType);
        var name = write.AuthorType switch
        {
            "staff" => await store.StaffName(id, ct) ?? throw new BusinessException("此帳號尚未綁定店員。"),
            "mascot" => (await store.Settings(ct)).MascotName,
            "anonymous_staff" => "匿名店員",
            _ => throw new BusinessException("請選擇有效的發言身分。")
        };
        return await store.Create(thread, GuestbookBoardService.Text(name, 60), GuestbookBoardService.Text(write.Content, 2000), write.AuthorType, id, write.AuthorType == "staff" ? staffId : null, null, ct);
    }
    public Task<GuestbookMessage> Edit(string thread, string? reply, GuestbookEdit edit, ClaimsPrincipal actor, CancellationToken ct)
        => store.Change(thread, reply, Actor(actor), new GuestbookEdit { DisplayName = GuestbookBoardService.Text(edit.DisplayName, 60), Content = GuestbookBoardService.Text(edit.Content, 2000), Version = edit.Version }, null, ct);
    public Task<GuestbookMessage> Moderate(string thread, string? reply, GuestbookModerate change, ClaimsPrincipal actor, CancellationToken ct)
        => store.Change(thread, reply, Actor(actor), null, change, ct);
    public Task Reorder(GuestbookPinOrder order, ClaimsPrincipal actor, CancellationToken ct)
        => store.Reorder(order.Items ?? throw new BusinessException("缺少排序清單。"), Actor(actor), ct);
    public Task<GuestbookSettings> Settings(CancellationToken ct) => store.Settings(ct);
    public Task SaveSettings(GuestbookSettings settings, ClaimsPrincipal actor, CancellationToken ct)
        => store.SaveSettings(new(GuestbookBoardService.Text(settings.MascotName, 60), settings.Version), Actor(actor), ct);
    public Task<IReadOnlyList<object>> History(string id, CancellationToken ct) => store.History(id, ct);
}
