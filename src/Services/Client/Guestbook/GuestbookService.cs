using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Guestbook;

// Compatibility facade: legacy write routes use the same validation and cooldown.
public sealed class GuestbookService(GuestbookBoardService board, ToBeClarify.Api.Repositories.Client.Guestbook.IGuestbookRepository repository) : IGuestbookService
{
    public async Task<GuestbookPageDto> GetGuestbookCommentsAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
            throw new ToBeClarify.Api.Exceptions.BusinessException("分頁參數不正確。", "INVALID_PAGE");
        var count = await repository.CountGuestbookCommentsAsync(ct);
        var rows = await repository.GetGuestbookCommentsAsync((page - 1) * pageSize, pageSize, ct);
        var replies = await repository.GetGuestbookRepliesAsync(rows.Select(row => row.Id).ToArray(), ct);
        return new(page, pageSize, count, ClientContentMappings.MapGuestbookComments(rows, replies));
    }
    public async Task<GuestbookCommentDto> GetGuestbookCommentAsync(string id, CancellationToken ct)
    {
        var row = await repository.GetGuestbookCommentAsync(id, ct)
            ?? throw new ToBeClarify.Api.Exceptions.NotFoundException("找不到留言。", "GUESTBOOK_NOT_FOUND");
        var replies = await repository.GetGuestbookRepliesAsync([id], ct);
        return ClientContentMappings.MapGuestbookComments([row], replies)[0];
    }
    public async Task<GuestbookCommentDto> CreateGuestbookCommentAsync(CreateGuestbookCommentRequest request, CancellationToken ct)
        => Map(await board.Create(null, new GuestbookWrite { DisplayName = request.DisplayName, Content = request.Content, Website = request.Website }, ct));
    public async Task<GuestbookReplyDto> CreateGuestbookReplyAsync(string id, CreateGuestbookReplyRequest request, CancellationToken ct)
        => MapReply(await board.Create(id, new GuestbookWrite { DisplayName = request.DisplayName, Content = request.Content, Website = request.Website }, ct));
    private static GuestbookCommentDto Map(GuestbookMessage m) => new(m.Id, m.DisplayName, m.Content, m.IsPinned, ClientContentMappings.ToTaiwanOffset(m.CreatedAt), []);
    private static GuestbookReplyDto MapReply(GuestbookMessage m) => new(m.Id, m.DisplayName, m.Content, ClientContentMappings.ToTaiwanOffset(m.CreatedAt));
}
