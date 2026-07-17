using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Client.Guestbook;
using ToBeClarify.Api.Services.Client.Shared;

namespace ToBeClarify.Api.Services.Client.Guestbook;

public sealed class GuestbookService : IGuestbookService
{
    private readonly IGuestbookRepository _repository;
    private readonly IAppClock _clock;

    public GuestbookService(IGuestbookRepository repository, IAppClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<GuestbookPageDto> GetGuestbookCommentsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1) throw new BusinessException("Page must be at least 1.", "INVALID_PAGE");
        if (pageSize is < 1 or > 100) throw new BusinessException("Page size must be between 1 and 100.", "INVALID_PAGE_SIZE");
        var totalTask = _repository.CountGuestbookCommentsAsync(cancellationToken);
        var commentsTask = _repository.GetGuestbookCommentsAsync((page - 1) * pageSize, pageSize, cancellationToken);
        await Task.WhenAll(totalTask, commentsTask);
        var comments = await commentsTask;
        var replies = await _repository.GetGuestbookRepliesAsync(comments.Select(row => row.Id).ToArray(), cancellationToken);
        return new GuestbookPageDto(page, pageSize, await totalTask, ClientContentMappings.MapGuestbookComments(comments, replies));
    }

    public async Task<GuestbookCommentDto> GetGuestbookCommentAsync(string id, CancellationToken cancellationToken)
    {
        var row = await _repository.GetGuestbookCommentAsync(ClientContentMappings.RequiredId(id), cancellationToken)
            ?? throw new NotFoundException("Guestbook comment not found.", "GUESTBOOK_COMMENT_NOT_FOUND");
        var replies = await _repository.GetGuestbookRepliesAsync([row.Id], cancellationToken);
        return ClientContentMappings.MapGuestbookComments([row], replies).Single();
    }

    public async Task<GuestbookCommentDto> CreateGuestbookCommentAsync(CreateGuestbookCommentRequest request, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var displayName = ClientContentMappings.CleanUserText(request.DisplayName, 60, "INVALID_DISPLAY_NAME");
        var content = ClientContentMappings.CleanUserText(request.Content, 5000, "INVALID_CONTENT");
        var userToken = ClientContentMappings.HashUserToken(ClientContentMappings.OptionalUserText(request.UserToken, 120, "INVALID_USER_TOKEN"));
        var now = _clock.LocalDateTime;
        await _repository.InsertGuestbookCommentAsync(id, displayName, userToken, content, now, cancellationToken);
        return new GuestbookCommentDto(id, displayName, content, false, ClientContentMappings.ToTaiwanOffset(now), Array.Empty<GuestbookReplyDto>());
    }

    public async Task<GuestbookReplyDto> CreateGuestbookReplyAsync(string commentId, CreateGuestbookReplyRequest request, CancellationToken cancellationToken)
    {
        var parentId = ClientContentMappings.RequiredId(commentId);
        var id = Guid.NewGuid().ToString();
        var displayName = ClientContentMappings.CleanUserText(request.DisplayName, 60, "INVALID_DISPLAY_NAME");
        var content = ClientContentMappings.CleanUserText(request.Content, 5000, "INVALID_CONTENT");
        var userToken = ClientContentMappings.HashUserToken(ClientContentMappings.OptionalUserText(request.UserToken, 120, "INVALID_USER_TOKEN"));
        var now = _clock.LocalDateTime;
        var created = await _repository.InsertGuestbookReplyAsync(id, parentId, displayName, userToken, content, now, cancellationToken);
        if (!created) throw new NotFoundException("Guestbook comment not found.", "GUESTBOOK_COMMENT_NOT_FOUND");
        return new GuestbookReplyDto(id, displayName, content, ClientContentMappings.ToTaiwanOffset(now));
    }
}
