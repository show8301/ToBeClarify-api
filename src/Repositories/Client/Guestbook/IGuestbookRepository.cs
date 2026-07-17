using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Guestbook;

public interface IGuestbookRepository
{
    Task<int> CountGuestbookCommentsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GuestbookCommentRow>> GetGuestbookCommentsAsync(int offset, int pageSize, CancellationToken cancellationToken);
    Task<GuestbookCommentRow?> GetGuestbookCommentAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GuestbookReplyRow>> GetGuestbookRepliesAsync(IReadOnlyCollection<string> commentIds, CancellationToken cancellationToken);
    Task InsertGuestbookCommentAsync(string id, string displayName, string? userToken, string content, DateTime now, CancellationToken cancellationToken);
    Task<bool> InsertGuestbookReplyAsync(string id, string commentId, string displayName, string? userToken, string content, DateTime now, CancellationToken cancellationToken);
}
