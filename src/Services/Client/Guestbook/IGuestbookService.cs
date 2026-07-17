using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Client.Guestbook;

public interface IGuestbookService
{
    Task<GuestbookPageDto> GetGuestbookCommentsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<GuestbookCommentDto> GetGuestbookCommentAsync(string id, CancellationToken cancellationToken);
    Task<GuestbookCommentDto> CreateGuestbookCommentAsync(CreateGuestbookCommentRequest request, CancellationToken cancellationToken);
    Task<GuestbookReplyDto> CreateGuestbookReplyAsync(string commentId, CreateGuestbookReplyRequest request, CancellationToken cancellationToken);
}
