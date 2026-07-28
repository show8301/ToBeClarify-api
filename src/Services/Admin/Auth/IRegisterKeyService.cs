using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Auth;

public interface IRegisterKeyService
{
    Task<AdminRegisterKeyDto> IssueAsync(CancellationToken cancellationToken);

    Task<T> ConsumeAsync<T>(
        string submittedKey,
        Func<Task<T>> registration,
        CancellationToken cancellationToken);
}
