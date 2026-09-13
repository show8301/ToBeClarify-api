using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Guestbook;

public interface IGuestbookRepository
{
    Task<int> CountGuestbookCommentsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GuestbookCommentRow>> GetGuestbookCommentsAsync(int offset, int pageSize, CancellationToken cancellationToken);
    Task<GuestbookCommentRow?> GetGuestbookCommentAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GuestbookReplyRow>> GetGuestbookRepliesAsync(IReadOnlyCollection<string> commentIds, CancellationToken cancellationToken);
}
